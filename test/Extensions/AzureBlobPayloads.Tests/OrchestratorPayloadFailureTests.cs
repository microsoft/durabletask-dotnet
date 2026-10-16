// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.DurableTask.Entities;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc;
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
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task PermanentPayloadFailure_ReleasesEntitiesBeforeSendingFailedCompletion(
        bool completeOrchestration,
        bool streamHistory)
    {
        // Arrange
        const string InstanceId = "locked-payload-instance";
        const string ExecutionId = "execution-1";
        const string OrchestratorName = "LockedPayloadOrchestrator";
        EntityInstanceId[] entities = [new("counter", "one"), new("counter", "two")];
        string oversizedPayload = new('x', GrpcDurableTaskWorkerOptions.MinCompleteOrchestrationWorkItemChunkSizeInBytes + 1024);
        Channel<P.WorkItem> workItems = Channel.CreateUnbounded<P.WorkItem>();
        Channel<P.HistoryChunk> history = Channel.CreateUnbounded<P.HistoryChunk>();
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
            .Returns(() => new AsyncServerStreamingCall<P.HistoryChunk>(
                new ChannelStreamReader<P.HistoryChunk>(history.Reader),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));
        invoker.Setup(value => value.AsyncUnaryCall(
                It.IsAny<Method<P.OrchestratorResponse, P.CompleteTaskResponse>>(),
                It.IsAny<string>(),
                It.IsAny<CallOptions>(),
                It.IsAny<P.OrchestratorResponse>()))
            .Callback<Method<P.OrchestratorResponse, P.CompleteTaskResponse>, string, CallOptions, P.OrchestratorResponse>(
                (_, _, _, response) => completions.Writer.TryWrite(response.Clone()))
            .Returns(() => CreateUnaryCall(new P.CompleteTaskResponse()));
        Mock<PayloadStore> store = new(MockBehavior.Strict);
        store.Setup(value => value.IsKnownPayloadToken(It.IsAny<string>())).Returns(false);
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
                options.CompleteOrchestrationWorkItemChunkSizeInBytes =
                    GrpcDurableTaskWorkerOptions.MinCompleteOrchestrationWorkItemChunkSizeInBytes;
            });
            builder.UseExternalizedPayloads(options =>
            {
                options.ThresholdBytes = 1024;
                options.MaxPayloadBytes = 2048;
            });
            builder.AddTasks(tasks => tasks.AddOrchestratorFunc<object?, string>(
                OrchestratorName,
                async (context, _) =>
                {
                    await using IAsyncDisposable locks = await context.Entities.LockEntitiesAsync(entities);
                    if (!completeOrchestration)
                    {
                        await context.Entities.CallEntityAsync(entities[0], "set", oversizedPayload);
                    }

                    return oversizedPayload;
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
            if (streamHistory)
            {
                await history.Writer.WriteAsync(
                    new P.HistoryChunk { Events = { resumedRequest.PastEvents } }, timeout.Token);
                history.Writer.Complete();
                resumedRequest.PastEvents.Clear();
                resumedRequest.RequiresHistoryStreaming = true;
            }

            // Act
            await workItems.Writer.WriteAsync(new P.WorkItem
            {
                OrchestratorRequest = resumedRequest,
                CompletionToken = "resumed-token",
            }, timeout.Token);
            P.OrchestratorResponse failed = await completions.Reader.ReadAsync(timeout.Token);

            // Assert
            P.CompleteOrchestrationAction completion = failed.Actions.Last().CompleteOrchestration;
            Assert.NotNull(completion);
            Assert.Equal(P.OrchestrationStatus.Failed, completion.OrchestrationStatus);
            Assert.Equal(typeof(PayloadStorageException).FullName, completion.FailureDetails.ErrorType);
            Assert.True(completion.FailureDetails.IsNonRetriable);
            Assert.Equal(entities.Length + 1, failed.Actions.Count);
            P.EntityUnlockSentEvent[] unlocks = failed.Actions.Take(entities.Length)
                .Select(action => action.SendEntityMessage.EntityUnlockSent).ToArray();
            Assert.Equal(entities.Select(entity => entity.ToString()), unlocks.Select(unlock => unlock.TargetInstanceId));
            Assert.All(unlocks, unlock =>
            {
                Assert.Equal(acquire.CriticalSectionId, unlock.CriticalSectionId);
                Assert.Equal(InstanceId, unlock.ParentInstanceId);
            });
            Assert.Equal("resumed-token", failed.CompletionToken);
            invoker.Verify(value => value.AsyncUnaryCall(
                It.IsAny<Method<P.OrchestratorResponse, P.CompleteTaskResponse>>(),
                It.IsAny<string>(),
                It.IsAny<CallOptions>(),
                It.IsAny<P.OrchestratorResponse>()), Times.Exactly(2));
            store.Verify(value => value.UploadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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
