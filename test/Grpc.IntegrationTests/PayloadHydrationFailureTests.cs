// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Azure;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Grpc.Tests;

public class PayloadHydrationFailureTests
{
    const string TokenPrefix = "test-blob://";
    const string BadToken = TokenPrefix + "bad";
    const string FailedCompletionToken = "failed-delivery";
    static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(15);

    public static IEnumerable<object[]> HydrationFailures()
    {
        P.WorkItem.RequestOneofCase[] kinds =
        [
            P.WorkItem.RequestOneofCase.ActivityRequest,
            P.WorkItem.RequestOneofCase.OrchestratorRequest,
            P.WorkItem.RequestOneofCase.EntityRequest,
            P.WorkItem.RequestOneofCase.EntityRequestV2,
        ];

        foreach (P.WorkItem.RequestOneofCase kind in kinds)
        {
            foreach (int storageStatus in new[] { 404, 403, 0 })
            {
                foreach (bool ownershipLost in new[] { false, true })
                {
                    yield return [kind, storageStatus, ownershipLost];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(HydrationFailures))]
    public async Task HydrationFailure_AbandonsOnlyFailedDeliveryAndKeepsStreamAsync(
        P.WorkItem.RequestOneofCase kind, int storageStatus, bool ownershipLost)
    {
        // Arrange
        using CancellationTokenSource timeout = new(TestTimeout);
        await using ScriptedSidecar sidecar = await ScriptedSidecar.StartAsync();
        sidecar.AbandonStatus = ownershipLost ? StatusCode.NotFound : StatusCode.OK;
        Exception failure = storageStatus == 0
            ? new IOException("Transient payload download failure.")
            : new RequestFailedException(storageStatus, "Payload download failed.");
        Mock<PayloadStore> store = CreatePayloadStore(failure);
        TaskCompletionSource running = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ConcurrentQueue<string> inputs = new();
        await using ServiceProvider provider = CreateWorker(sidecar, store.Object, async input =>
        {
            inputs.Enqueue(input);
            if (input == "running")
            {
                running.TrySetResult();
                await release.Task.WaitAsync(timeout.Token);
            }

            return input;
        });
        IHostedService worker = Assert.Single(provider.GetServices<IHostedService>());
        await worker.StartAsync(timeout.Token);

        try
        {
            // Act
            sidecar.Send(CreateHealthyActivity("running"));
            await running.Task.WaitAsync(timeout.Token);
            sidecar.Send(CreateFailedWorkItem(kind));
            sidecar.Send(new P.WorkItem { HealthPing = new P.HealthPing() });
            sidecar.Send(CreateHealthyActivity("following"));
            P.ActivityResponse following = await sidecar.Completions.Reader.ReadAsync(timeout.Token);
            release.TrySetResult();
            P.ActivityResponse alreadyRunning = await sidecar.Completions.Reader.ReadAsync(timeout.Token);

            // Assert
            Assert.Equal(1, sidecar.StreamCount);
            Assert.Equal(0, sidecar.ClosedStreamCount);
            Assert.Equal(1, sidecar.HelloCount);
            P.WorkItem.RequestOneofCase abandonKind = kind == P.WorkItem.RequestOneofCase.EntityRequestV2
                ? P.WorkItem.RequestOneofCase.EntityRequest
                : kind;
            Assert.Equal((abandonKind, FailedCompletionToken), Assert.Single(sidecar.Abandons));
            Assert.Equal(new[] { "running", "following" }, inputs.ToArray());
            Assert.Equal("following", following.CompletionToken);
            Assert.Equal("\"following\"", following.Result);
            Assert.Null(following.FailureDetails);
            Assert.Equal("running", alreadyRunning.CompletionToken);
            Assert.Equal("\"running\"", alreadyRunning.Result);
            Assert.Null(alreadyRunning.FailureDetails);
            store.Verify(s => s.DownloadAsync(BadToken, It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            release.TrySetResult();
            await worker.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
        }
    }

    [Theory]
    [InlineData(StatusCode.Unknown, false)]
    [InlineData(StatusCode.Unavailable, false)]
    [InlineData(StatusCode.PermissionDenied, false)]
    [InlineData(StatusCode.Internal, false)]
    [InlineData(StatusCode.Unavailable, true)]
    public async Task TransportOrAbandonFailure_ReconnectsAsync(StatusCode status, bool streamFailure)
    {
        // Arrange
        using CancellationTokenSource timeout = new(TestTimeout);
        await using ScriptedSidecar sidecar = await ScriptedSidecar.StartAsync();
        sidecar.AbandonStatus = status;
        Mock<PayloadStore> store = CreatePayloadStore(new IOException("Payload download failed."));
        await using ServiceProvider provider = CreateWorker(sidecar, store.Object);
        IHostedService worker = Assert.Single(provider.GetServices<IHostedService>());
        await worker.StartAsync(timeout.Token);

        try
        {
            Assert.Equal(1, await sidecar.Streams.Reader.ReadAsync(timeout.Token));

            // Act
            if (streamFailure)
            {
                sidecar.FailStream(status);
            }
            else
            {
                sidecar.Send(CreateFailedWorkItem(P.WorkItem.RequestOneofCase.ActivityRequest));
            }

            Assert.Equal(2, await sidecar.Streams.Reader.ReadAsync(timeout.Token));
            sidecar.Send(CreateHealthyActivity("following"));
            P.ActivityResponse response = await sidecar.Completions.Reader.ReadAsync(timeout.Token);

            // Assert
            Assert.Equal(2, sidecar.StreamCount);
            Assert.Equal(2, sidecar.HelloCount);
            Assert.Equal("following", response.CompletionToken);
            Assert.Null(response.FailureDetails);
            Assert.Equal(streamFailure ? 0 : 1, sidecar.Abandons.Count);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
        }
    }

    [Fact]
    public async Task HydrationFailure_RedeliveryCompletesAfterPayloadRecoversAsync()
    {
        // Arrange
        using CancellationTokenSource timeout = new(TestTimeout);
        await using ScriptedSidecar sidecar = await ScriptedSidecar.StartAsync();
        Mock<PayloadStore> store = CreatePayloadStore(new IOException("Payload download failed."));
        store.SetupSequence(s => s.DownloadAsync(BadToken, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Payload not found."))
            .ReturnsAsync("\"recovered\"");
        await using ServiceProvider provider = CreateWorker(sidecar, store.Object);
        IHostedService worker = Assert.Single(provider.GetServices<IHostedService>());
        await worker.StartAsync(timeout.Token);

        try
        {
            // Act
            sidecar.Send(CreateFailedWorkItem(P.WorkItem.RequestOneofCase.ActivityRequest));
            await sidecar.AbandonStarted.Task.WaitAsync(timeout.Token);
            P.WorkItem redelivery = CreateFailedWorkItem(P.WorkItem.RequestOneofCase.ActivityRequest);
            redelivery.CompletionToken = "redelivery";
            sidecar.Send(redelivery);
            P.ActivityResponse response = await sidecar.Completions.Reader.ReadAsync(timeout.Token);

            // Assert
            Assert.Equal(1, sidecar.StreamCount);
            Assert.Equal((P.WorkItem.RequestOneofCase.ActivityRequest, FailedCompletionToken), Assert.Single(sidecar.Abandons));
            Assert.Equal("redelivery", response.CompletionToken);
            Assert.Equal("\"recovered\"", response.Result);
            Assert.Null(response.FailureDetails);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShutdownDuringHydrationOrAbandon_DoesNotReconnectAsync(bool duringAbandon)
    {
        // Arrange
        using CancellationTokenSource timeout = new(TestTimeout);
        await using ScriptedSidecar sidecar = await ScriptedSidecar.StartAsync();
        sidecar.HoldAbandon = duringAbandon;
        TaskCompletionSource<CancellationToken> downloading = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<PayloadStore> store = CreatePayloadStore(new IOException("Payload download failed."));
        if (!duringAbandon)
        {
            store.Setup(s => s.DownloadAsync(BadToken, It.IsAny<CancellationToken>()))
                .Returns(async (string _, CancellationToken cancellation) =>
                {
                    downloading.TrySetResult(cancellation);
                    await Task.Delay(Timeout.Infinite, cancellation);
                    return string.Empty;
                });
        }

        await using ServiceProvider provider = CreateWorker(sidecar, store.Object);
        IHostedService worker = Assert.Single(provider.GetServices<IHostedService>());
        await worker.StartAsync(timeout.Token);

        try
        {
            sidecar.Send(CreateFailedWorkItem(P.WorkItem.RequestOneofCase.ActivityRequest));
            CancellationToken downloadCancellation = default;
            if (duringAbandon)
            {
                await sidecar.AbandonStarted.Task.WaitAsync(timeout.Token);
            }
            else
            {
                downloadCancellation = await downloading.Task.WaitAsync(timeout.Token);
            }

            // Act
            await worker.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

            // Assert
            Assert.Equal(1, sidecar.StreamCount);
            Assert.Equal(duringAbandon ? 1 : 0, sidecar.Abandons.Count);
            Assert.False(sidecar.Completions.Reader.TryRead(out _));
            if (!duringAbandon)
            {
                Assert.True(downloadCancellation.IsCancellationRequested);
            }
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonWorkItemHydrationFailure_PropagatesOriginalExceptionAsync(bool historyStream)
    {
        // Arrange
        using CancellationTokenSource timeout = new(TestTimeout);
        await using ScriptedSidecar sidecar = await ScriptedSidecar.StartAsync();
        RequestFailedException failure = new(403, "Payload download forbidden.");
        Mock<PayloadStore> store = CreatePayloadStore(failure);
        using GrpcChannel channel = GrpcChannel.ForAddress(sidecar.Address);
        P.TaskHubSidecarService.TaskHubSidecarServiceClient client = new(channel.Intercept(
            new AzureBlobPayloadsSideCarInterceptor(store.Object, new LargePayloadStorageOptions())));

        // Act
        Exception? observed;
        if (historyStream)
        {
            using AsyncServerStreamingCall<P.HistoryChunk> call = client.StreamInstanceHistory(
                new P.StreamInstanceHistoryRequest { InstanceId = "instance" }, cancellationToken: timeout.Token);
            observed = await Record.ExceptionAsync(() => call.ResponseStream.MoveNext(timeout.Token));
        }
        else
        {
            using AsyncUnaryCall<P.GetInstanceResponse> call = client.GetInstanceAsync(
                new P.GetInstanceRequest { InstanceId = "instance" }, cancellationToken: timeout.Token);
            observed = await Record.ExceptionAsync(async () => await call.ResponseAsync);
        }

        // Assert
        Assert.Same(failure, observed);
        Assert.Empty(sidecar.Abandons);
    }

    static Mock<PayloadStore> CreatePayloadStore(Exception failure)
    {
        Mock<PayloadStore> store = new();
        store.Setup(s => s.IsKnownPayloadToken(It.IsAny<string>()))
            .Returns((string value) => value.StartsWith(TokenPrefix, StringComparison.Ordinal));
        store.Setup(s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string token, CancellationToken _) => token == BadToken
                ? Task.FromException<string>(failure)
                : Task.FromResult(JsonSerializer.Serialize(token[TokenPrefix.Length..])));
        return store;
    }

    static ServiceProvider CreateWorker(ScriptedSidecar sidecar, PayloadStore store, Func<string, Task<string>>? runActivity = null)
    {
        ServiceCollection services = new();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddSingleton(store);
        services.AddDurableTaskWorker(builder =>
        {
            builder.UseGrpc(options =>
            {
                options.Address = sidecar.Address;
                options.Internal.TransientRetryMaxAttempts = 1;
                options.Internal.ReconnectBackoffBase = TimeSpan.Zero;
                options.Internal.ReconnectBackoffCap = TimeSpan.Zero;
            });
            builder.UseExternalizedPayloads();
            builder.AddTasks(tasks => tasks.AddActivityFunc<string, string>(
                "Healthy", (_, input) => runActivity?.Invoke(input!) ?? Task.FromResult(input!)));
        });
        return services.BuildServiceProvider();
    }

    static P.WorkItem CreateHealthyActivity(string name) => new()
    {
        CompletionToken = name,
        ActivityRequest = new P.ActivityRequest
        {
            Name = "Healthy",
            TaskId = 1,
            Input = TokenPrefix + name,
            OrchestrationInstance = new P.OrchestrationInstance { InstanceId = name, ExecutionId = "execution" },
        },
    };

    static P.WorkItem CreateFailedWorkItem(P.WorkItem.RequestOneofCase kind)
    {
        P.WorkItem item = new() { CompletionToken = FailedCompletionToken };
        switch (kind)
        {
            case P.WorkItem.RequestOneofCase.ActivityRequest:
                item.ActivityRequest = CreateHealthyActivity("failed").ActivityRequest;
                item.ActivityRequest.Input = BadToken;
                break;
            case P.WorkItem.RequestOneofCase.OrchestratorRequest:
                item.OrchestratorRequest = new P.OrchestratorRequest
                {
                    InstanceId = "failed",
                    PastEvents = { new P.HistoryEvent { ExecutionStarted = new P.ExecutionStartedEvent { Input = TokenPrefix + "state" } } },
                    NewEvents = { new P.HistoryEvent { EventRaised = new P.EventRaisedEvent { Name = "event", Input = BadToken } } },
                };
                break;
            case P.WorkItem.RequestOneofCase.EntityRequest:
                item.EntityRequest = new P.EntityBatchRequest
                {
                    InstanceId = "@entity@failed",
                    EntityState = TokenPrefix + "state",
                    Operations = { new P.OperationRequest { Operation = "operation", Input = BadToken } },
                };
                break;
            case P.WorkItem.RequestOneofCase.EntityRequestV2:
                item.EntityRequestV2 = new P.EntityRequest
                {
                    InstanceId = "@entity@failed",
                    EntityState = TokenPrefix + "state",
                    OperationRequests = { new P.HistoryEvent { EntityOperationSignaled = new P.EntityOperationSignaledEvent { Input = BadToken } } },
                };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported work item kind.");
        }

        return item;
    }

    sealed class ScriptedSidecar : P.TaskHubSidecarService.TaskHubSidecarServiceBase, IAsyncDisposable
    {
        readonly Channel<Func<IServerStreamWriter<P.WorkItem>, Task>> deliveries =
            Channel.CreateUnbounded<Func<IServerStreamWriter<P.WorkItem>, Task>>();
        IHost host = null!;
        int streamCount;
        int closedStreamCount;
        int helloCount;

        public string Address { get; private set; } = string.Empty;

        public StatusCode AbandonStatus { get; set; }

        public bool HoldAbandon { get; set; }

        public int StreamCount => Volatile.Read(ref this.streamCount);

        public int ClosedStreamCount => Volatile.Read(ref this.closedStreamCount);

        public int HelloCount => Volatile.Read(ref this.helloCount);

        public Channel<int> Streams { get; } = Channel.CreateUnbounded<int>();

        public Channel<P.ActivityResponse> Completions { get; } = Channel.CreateUnbounded<P.ActivityResponse>();

        public ConcurrentQueue<(P.WorkItem.RequestOneofCase Kind, string Token)> Abandons { get; } = new();

        public TaskCompletionSource AbandonStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static async Task<ScriptedSidecar> StartAsync()
        {
            ScriptedSidecar sidecar = new();
            sidecar.host = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging => logging.ClearProviders())
                .ConfigureWebHostDefaults(web => web
                    .UseKestrel(options => options.Listen(IPAddress.Loopback, 0, endpoint => endpoint.Protocols = HttpProtocols.Http2))
                    .ConfigureServices(services =>
                    {
                        services.AddGrpc();
                        services.AddSingleton(sidecar);
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints => endpoints.MapGrpcService<ScriptedSidecar>());
                    }))
                .Build();
            await sidecar.host.StartAsync().WaitAsync(TestTimeout);
            sidecar.Address = Assert.Single(sidecar.host.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses);
            return sidecar;
        }

        public void Send(P.WorkItem item) => Assert.True(this.deliveries.Writer.TryWrite(stream => stream.WriteAsync(item)));

        public void FailStream(StatusCode status) => Assert.True(this.deliveries.Writer.TryWrite(
            _ => Task.FromException(new RpcException(new Status(status, "Stream transport failed.")))));

        public override Task<Empty> Hello(Empty request, ServerCallContext context)
        {
            Interlocked.Increment(ref this.helloCount);
            return Task.FromResult(new Empty());
        }

        public override async Task GetWorkItems(P.GetWorkItemsRequest request, IServerStreamWriter<P.WorkItem> responseStream, ServerCallContext context)
        {
            await context.WriteResponseHeadersAsync(new Metadata());
            this.Streams.Writer.TryWrite(Interlocked.Increment(ref this.streamCount));
            try
            {
                await foreach (Func<IServerStreamWriter<P.WorkItem>, Task> deliver in this.deliveries.Reader.ReadAllAsync(context.CancellationToken))
                {
                    await deliver(responseStream);
                }
            }
            finally
            {
                Interlocked.Increment(ref this.closedStreamCount);
            }
        }

        public override Task<P.CompleteTaskResponse> CompleteActivityTask(P.ActivityResponse request, ServerCallContext context)
        {
            this.Completions.Writer.TryWrite(request);
            return Task.FromResult(new P.CompleteTaskResponse());
        }

        public override async Task<P.AbandonActivityTaskResponse> AbandonTaskActivityWorkItem(P.AbandonActivityTaskRequest request, ServerCallContext context)
        {
            await this.AbandonAsync(P.WorkItem.RequestOneofCase.ActivityRequest, request.CompletionToken, context);
            return new P.AbandonActivityTaskResponse();
        }

        public override async Task<P.AbandonOrchestrationTaskResponse> AbandonTaskOrchestratorWorkItem(P.AbandonOrchestrationTaskRequest request, ServerCallContext context)
        {
            await this.AbandonAsync(P.WorkItem.RequestOneofCase.OrchestratorRequest, request.CompletionToken, context);
            return new P.AbandonOrchestrationTaskResponse();
        }

        public override async Task<P.AbandonEntityTaskResponse> AbandonTaskEntityWorkItem(P.AbandonEntityTaskRequest request, ServerCallContext context)
        {
            await this.AbandonAsync(P.WorkItem.RequestOneofCase.EntityRequest, request.CompletionToken, context);
            return new P.AbandonEntityTaskResponse();
        }

        public override Task<P.GetInstanceResponse> GetInstance(P.GetInstanceRequest request, ServerCallContext context)
            => Task.FromResult(new P.GetInstanceResponse { OrchestrationState = new P.OrchestrationState { Input = BadToken } });

        public override Task StreamInstanceHistory(P.StreamInstanceHistoryRequest request, IServerStreamWriter<P.HistoryChunk> responseStream, ServerCallContext context)
            => responseStream.WriteAsync(new P.HistoryChunk
            {
                Events = { new P.HistoryEvent { ExecutionStarted = new P.ExecutionStartedEvent { Input = BadToken } } },
            });

        public async ValueTask DisposeAsync()
        {
            using IHost host = this.host;
            await host.StopAsync().WaitAsync(TestTimeout);
        }

        async Task AbandonAsync(P.WorkItem.RequestOneofCase kind, string token, ServerCallContext context)
        {
            this.Abandons.Enqueue((kind, token));
            this.AbandonStarted.TrySetResult();
            if (this.HoldAbandon)
            {
                await Task.Delay(Timeout.Infinite, context.CancellationToken);
            }

            if (this.AbandonStatus != StatusCode.OK)
            {
                throw new RpcException(new Status(this.AbandonStatus, "Abandon failed."));
            }
        }
    }
}
