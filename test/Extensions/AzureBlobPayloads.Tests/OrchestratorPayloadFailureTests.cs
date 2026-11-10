// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Threading.Channels;
using Azure;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.DurableTask.Entities;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests;

public sealed class OrchestratorPayloadFailureTests
{
    enum PayloadFailure
    {
        None,
        SizeLimit,
        ForbiddenUpload,
        InlineSizeLimit,
        MissingDownload,
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public Task PermanentPayloadFailure_ReleasesEntitiesBeforeSendingFailedCompletion(
        bool completeOrchestration,
        bool streamHistory,
        bool failUpload)
        => RunLockedOrchestrationAsync(
            completeOrchestration,
            streamHistory,
            failUpload ? PayloadFailure.ForbiddenUpload : PayloadFailure.SizeLimit);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task SuccessfulCompletion_ReleasesEntitiesExactlyOnceBeforeCompletion(
        bool useExternalizedPayloads,
        bool explicitlyReleaseLocks)
        => RunLockedOrchestrationAsync(
            completeOrchestration: true,
            streamHistory: false,
            failure: PayloadFailure.None,
            useExternalizedPayloads: useExternalizedPayloads,
            explicitlyReleaseLocks: explicitlyReleaseLocks);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task MissingPayloadDownload_ReleasesEntitiesBeforeFailingAsync(bool streamHistory, bool historyPayload)
        => RunLockedOrchestrationAsync(
            completeOrchestration: true,
            streamHistory,
            PayloadFailure.MissingDownload,
            historyPayload: historyPayload);

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public Task InlineSizeFailure_ReleasesEntitiesBeforeSendingFailedCompletion(
        bool completeOrchestration,
        bool streamHistory,
        bool explicitlyReleaseLocks)
        => RunLockedOrchestrationAsync(
            completeOrchestration,
            streamHistory,
            PayloadFailure.InlineSizeLimit,
            useExternalizedPayloads: false,
            explicitlyReleaseLocks: explicitlyReleaseLocks);

    static async Task RunLockedOrchestrationAsync(
        bool completeOrchestration,
        bool streamHistory,
        PayloadFailure failure,
        bool useExternalizedPayloads = true,
        bool explicitlyReleaseLocks = true,
        bool historyPayload = false)
    {
        // Arrange
        const string InstanceId = "locked-payload-instance";
        const string ExecutionId = "execution-1";
        const string OrchestratorName = "LockedPayloadOrchestrator";
        EntityInstanceId[] entities = [new("counter", "one"), new("counter", "two")];
        string payload = failure switch
        {
            PayloadFailure.None => "completed",
            PayloadFailure.InlineSizeLimit => new('x', 5 * 1024 * 1024),
            _ => new('x', 1024 * 1024 + 1024),
        };
        string serializedPayload = JsonSerializer.Serialize(payload);
        Channel<P.WorkItem> workItems = Channel.CreateUnbounded<P.WorkItem>();
        P.HistoryChunk? streamedHistory = null;
        Channel<P.OrchestratorResponse> completions = Channel.CreateUnbounded<P.OrchestratorResponse>();
        Mock<CallInvoker> invoker = new(MockBehavior.Strict);
        invoker.Setup(value => value.AsyncUnaryCall(
                It.IsAny<Method<Empty, Empty>>(),
                It.IsAny<string>(),
                It.IsAny<CallOptions>(),
                It.IsAny<Empty>()))
            .Returns(() => CreateUnaryCall(new Empty()));
        invoker.Setup(value => value.AsyncServerStreamingCall(
                It.IsAny<Method<P.GetWorkItemsRequest, P.WorkItem>>(),
                It.IsAny<string>(),
                It.IsAny<CallOptions>(),
                It.IsAny<P.GetWorkItemsRequest>()))
            .Returns(() => new AsyncServerStreamingCall<P.WorkItem>(
                new ChannelStreamReader<P.WorkItem>(workItems.Reader),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));
        invoker.Setup(value => value.AsyncServerStreamingCall(
                It.IsAny<Method<P.StreamInstanceHistoryRequest, P.HistoryChunk>>(),
                It.IsAny<string>(),
                It.IsAny<CallOptions>(),
                It.IsAny<P.StreamInstanceHistoryRequest>()))
            .Returns(() =>
            {
                Channel<P.HistoryChunk> replay = Channel.CreateUnbounded<P.HistoryChunk>();
                replay.Writer.TryWrite(Assert.IsType<P.HistoryChunk>(streamedHistory).Clone());
                replay.Writer.Complete();
                return new AsyncServerStreamingCall<P.HistoryChunk>(
                    new ChannelStreamReader<P.HistoryChunk>(replay.Reader),
                    Task.FromResult(new Metadata()),
                    () => Status.DefaultSuccess,
                    () => new Metadata(),
                    () => { });
            });
        invoker.Setup(value => value.AsyncUnaryCall(
                It.IsAny<Method<P.OrchestratorResponse, P.CompleteTaskResponse>>(),
                It.IsAny<string>(),
                It.IsAny<CallOptions>(),
                It.IsAny<P.OrchestratorResponse>()))
            .Callback<Method<P.OrchestratorResponse, P.CompleteTaskResponse>, string, CallOptions, P.OrchestratorResponse>(
                (_, _, _, response) => completions.Writer.TryWrite(response.Clone()))
            .Returns(() => CreateUnaryCall(new P.CompleteTaskResponse()));
        Mock<PayloadStore> store = new(MockBehavior.Strict);
        store.Setup(value => value.IsKnownPayloadToken(It.IsAny<string>()))
            .Returns((string value) => value == "blob:v1:missing");
        store.Setup(value => value.DownloadAsync("blob:v1:missing", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PayloadStorageException("Payload missing.", new RequestFailedException(404, "Blob not found.")));
        if (failure == PayloadFailure.ForbiddenUpload)
        {
            store.Setup(value => value.UploadAsync(serializedPayload, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(403, "Simulated permanent Blob upload failure."));
        }

        ServiceCollection services = new();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(store.Object);
        services.AddDurableTaskWorker(builder =>
        {
            builder.Configure(options => options.EnableEntitySupport = true);
            builder.UseGrpc(options =>
            {
                options.CallInvoker = invoker.Object;
                options.ConfigureForAzureManaged();
            });
            if (useExternalizedPayloads)
            {
                builder.UseExternalizedPayloads(options =>
                {
                    options.ThresholdBytes = 1024;
                    options.MaxPayloadBytes = failure == PayloadFailure.ForbiddenUpload
                        ? 2 * 1024 * 1024
                        : 2048;
                });
            }

            builder.AddTasks(tasks => tasks.AddOrchestratorFunc<object?, string>(
                OrchestratorName,
                async (context, _) =>
                {
                    IAsyncDisposable locks = await context.Entities.LockEntitiesAsync(entities);
                    await using IAsyncDisposable? releasedLocks = explicitlyReleaseLocks ? locks : null;
                    if (failure == PayloadFailure.SizeLimit)
                    {
                        context.SetCustomStatus(new string('s', 1024));
                    }

                    if (!completeOrchestration)
                    {
                        await context.Entities.CallEntityAsync(entities[0], "set", payload);
                    }

                    return payload;
                }));
        });
        await using ServiceProvider provider = services.BuildServiceProvider();
        IHostedService worker = Assert.Single(provider.GetServices<IHostedService>());
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        DateTime timestamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        P.OrchestratorRequest initialRequest = new()
        {
            InstanceId = InstanceId,
            ExecutionId = ExecutionId,
            EntityParameters = new P.OrchestratorEntityParameters
            {
                EntityMessageReorderWindow = Duration.FromTimeSpan(TimeSpan.Zero),
            },
            NewEvents =
            {
                new P.HistoryEvent
                {
                    EventId = -1,
                    Timestamp = Timestamp.FromDateTime(timestamp),
                    OrchestratorStarted = new P.OrchestratorStartedEvent(),
                },
                new P.HistoryEvent
                {
                    EventId = -1,
                    Timestamp = Timestamp.FromDateTime(timestamp),
                    ExecutionStarted = new P.ExecutionStartedEvent
                    {
                        Name = OrchestratorName,
                        OrchestrationInstance = new P.OrchestrationInstance
                        {
                            InstanceId = InstanceId,
                            ExecutionId = ExecutionId,
                        },
                    },
                },
            },
        };
        await worker.StartAsync(timeout.Token);
        try
        {
            await workItems.Writer.WriteAsync(new P.WorkItem
            {
                OrchestratorRequest = initialRequest,
                CompletionToken = "acquire-token",
            }, timeout.Token);
            P.OrchestratorResponse acquiring = await completions.Reader.ReadAsync(timeout.Token);
            P.OrchestratorAction acquireAction = Assert.Single(acquiring.Actions);
            Assert.True(acquireAction.SendEntityMessage is not null, acquiring.ToString());
            P.EntityLockRequestedEvent acquire = acquireAction.SendEntityMessage.EntityLockRequested;
            Assert.NotNull(acquire);
            P.OrchestratorRequest resumedRequest = new()
            {
                InstanceId = InstanceId,
                ExecutionId = ExecutionId,
                EntityParameters = initialRequest.EntityParameters.Clone(),
                PastEvents =
                {
                    initialRequest.NewEvents,
                    new P.HistoryEvent
                    {
                        EventId = acquireAction.Id,
                        Timestamp = Timestamp.FromDateTime(timestamp),
                        EntityLockRequested = acquire.Clone(),
                    },
                    new P.HistoryEvent
                    {
                        EventId = -1,
                        Timestamp = Timestamp.FromDateTime(timestamp),
                        OrchestratorCompleted = new P.OrchestratorCompletedEvent(),
                    },
                },
                NewEvents =
                {
                    new P.HistoryEvent
                    {
                        EventId = -1,
                        Timestamp = Timestamp.FromDateTime(timestamp.AddSeconds(1)),
                        OrchestratorStarted = new P.OrchestratorStartedEvent(),
                    },
                    new P.HistoryEvent
                    {
                        EventId = -1,
                        Timestamp = Timestamp.FromDateTime(timestamp.AddSeconds(1)),
                        EntityLockGranted = new P.EntityLockGrantedEvent
                        {
                            CriticalSectionId = acquire.CriticalSectionId,
                        },
                    },
                },
            };
            if (failure == PayloadFailure.MissingDownload)
            {
                if (historyPayload)
                {
                    resumedRequest.PastEvents.Single(e => e.ExecutionStarted is not null).ExecutionStarted.Input = "blob:v1:missing";
                }
                else
                {
                    resumedRequest.NewEvents.Add(new P.HistoryEvent
                    {
                        EventRaised = new P.EventRaisedEvent { Name = "missing", Input = "blob:v1:missing" },
                    });
                }
            }

            if (streamHistory)
            {
                streamedHistory = new P.HistoryChunk { Events = { resumedRequest.PastEvents } };
                resumedRequest.PastEvents.Clear();
                resumedRequest.RequiresHistoryStreaming = true;
            }

            // Act
            await workItems.Writer.WriteAsync(new P.WorkItem
            {
                OrchestratorRequest = resumedRequest,
                CompletionToken = "resumed-token",
            }, timeout.Token);
            P.OrchestratorResponse response = await completions.Reader.ReadAsync(timeout.Token);

            // Assert
            P.OrchestratorAction completionAction = Assert.Single(
                response.Actions, action => action.CompleteOrchestration is not null);
            Assert.Same(completionAction, response.Actions.Last());
            P.CompleteOrchestrationAction completion = completionAction.CompleteOrchestration;
            if (failure == PayloadFailure.None)
            {
                Assert.Equal(P.OrchestrationStatus.Completed, completion.OrchestrationStatus);
                Assert.Null(completion.FailureDetails);
                Assert.Equal(serializedPayload, completion.Result);
            }
            else
            {
                Assert.Equal(P.OrchestrationStatus.Failed, completion.OrchestrationStatus);
                Assert.Equal(
                    failure switch
                    {
                        PayloadFailure.ForbiddenUpload => typeof(RequestFailedException).FullName,
                        PayloadFailure.InlineSizeLimit => typeof(InvalidOperationException).FullName,
                        _ => typeof(PayloadStorageException).FullName,
                    },
                    completion.FailureDetails.ErrorType);
                Assert.True(completion.FailureDetails.IsNonRetriable);
                Assert.Null(completion.Result);
            }

            Assert.Equal(entities.Length + 1, response.Actions.Count);
            P.EntityUnlockSentEvent[] unlocks = response.Actions.Take(entities.Length)
                .Select(action => Assert.IsType<P.EntityUnlockSentEvent>(action.SendEntityMessage?.EntityUnlockSent))
                .ToArray();
            Assert.Equal(entities.Select(entity => entity.ToString()), unlocks.Select(unlock => unlock.TargetInstanceId));
            Assert.All(unlocks, unlock =>
            {
                Assert.Equal(acquire.CriticalSectionId, unlock.CriticalSectionId);
                Assert.Equal(InstanceId, unlock.ParentInstanceId);
            });
            Assert.Equal(InstanceId, response.InstanceId);
            Assert.Equal("resumed-token", response.CompletionToken);
            Assert.Null(response.CustomStatus);
            Assert.True(response.CalculateSize() < 4 * 1024 * 1024);
            invoker.Verify(value => value.AsyncUnaryCall(
                It.IsAny<Method<P.OrchestratorResponse, P.CompleteTaskResponse>>(),
                It.IsAny<string>(),
                It.IsAny<CallOptions>(),
                It.IsAny<P.OrchestratorResponse>()), Times.Exactly(2));
            store.Verify(
                value => value.UploadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                failure == PayloadFailure.ForbiddenUpload ? Times.Once() : Times.Never());
            if (failure == PayloadFailure.MissingDownload)
            {
                store.Verify(value => value.DownloadAsync("blob:v1:missing", It.IsAny<CancellationToken>()), Times.Once);
            }
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    static AsyncUnaryCall<T> CreateUnaryCall<T>(T response)
        => new(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    sealed class ChannelStreamReader<T>(ChannelReader<T> reader) : IAsyncStreamReader<T>
    {
        public T Current { get; private set; } = default!;

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (await reader.WaitToReadAsync(cancellationToken))
            {
                this.Current = await reader.ReadAsync(cancellationToken);
                return true;
            }

            return false;
        }
    }
}
