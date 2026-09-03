// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Client.Grpc;
using Microsoft.DurableTask.Client.Grpc.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using LP = Microsoft.DurableTask.Protobuf.LargePayloads;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests.AutoPurge;

/// <summary>
/// Covers the public, explicitly-invoked auto-purge control. One call owns one task-hub transition, and it is
/// two operations rather than one: the backend setting is written first and awaited, then the singleton job
/// entity is signalled. Almost everything worth asserting here is about that boundary - what runs before the
/// backend is touched, what does not run when the first operation fails, and what is deliberately NOT undone
/// when the second one does.
/// </summary>
public class SetLargePayloadAutoPurgeAsyncTests
{
    const string ExpectedEntityId = "@blobpurgejob@__dt_blob_payload_autopurge__";

    /// <summary>
    /// The gRPC client's default number of consecutive transport failures before it recreates its channel.
    /// Mirrored here rather than configured because the internal option is not reachable from this assembly.
    /// </summary>
    const int ConsecutiveFailuresBeforeRecreate = 5;

    [Fact]
    public async Task Enable_WritesTheBackendSettingBeforeSignallingCreate()
    {
        // Arrange - both operations ride the same invoker, so their relative order is directly observable.
        RecordingCallInvoker invoker = new();
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        await client.SetLargePayloadAutoPurgeAsync(enabled: true);

        // Assert - the order is the point: enabling the setting before starting the job means the job cannot
        // poll for tombstones the backend is not yet writing.
        invoker.Methods.Should().Equal("SetLargePayloadAutoPurge", "SignalEntity");
        invoker.Sets.Should().ContainSingle().Which.Enabled.Should().BeTrue();

        P.SignalEntityRequest signal = invoker.Signals.Should().ContainSingle().Subject;
        signal.InstanceId.Should().Be(ExpectedEntityId);
        signal.Name.Should().Be("Create");
        signal.Input.Should().Be("500");
    }

    [Fact]
    public async Task Enable_PassesTheRequestedBatchSizeToTheJob()
    {
        // Arrange
        RecordingCallInvoker invoker = new();
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        await client.SetLargePayloadAutoPurgeAsync(enabled: true, batchSize: 42);

        // Assert - the size is the operation's input, so a caller resizing a running job does it with the same
        // call that started it.
        invoker.Signals.Should().ContainSingle().Which.Input.Should().Be("42");
    }

    [Fact]
    public async Task Disable_WritesTheBackendSettingBeforeSignallingStop()
    {
        // Arrange
        RecordingCallInvoker invoker = new();
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        await client.SetLargePayloadAutoPurgeAsync(enabled: false);

        // Assert - the mirror-image order: turning tombstoning off first means no new tombstones are created
        // while the job winds down.
        invoker.Methods.Should().Equal("SetLargePayloadAutoPurge", "SignalEntity");
        invoker.Sets.Should().ContainSingle().Which.Enabled.Should().BeFalse();

        P.SignalEntityRequest signal = invoker.Signals.Should().ContainSingle().Subject;
        signal.InstanceId.Should().Be(ExpectedEntityId);
        signal.Name.Should().Be("Stop");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public async Task Disable_IgnoresTheBatchSizeEvenWhenItIsOutOfRange(int batchSize)
    {
        // Arrange - the caller who threads a batch size through from configuration and flips only the flag.
        // Rejecting the size on the disable path would fail a call that would otherwise have done exactly what
        // was asked, since a job being stopped has no cycle to size.
        RecordingCallInvoker invoker = new();
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        await client.SetLargePayloadAutoPurgeAsync(enabled: false, batchSize: batchSize);

        // Assert
        invoker.Methods.Should().Equal("SetLargePayloadAutoPurge", "SignalEntity");
        invoker.Signals.Should().ContainSingle().Which.Name.Should().Be("Stop");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public async Task Enable_WithAnOutOfRangeBatchSize_TouchesNothing(int batchSize)
    {
        // Arrange
        RecordingCallInvoker invoker = new();
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        Func<Task> act = () => client.SetLargePayloadAutoPurgeAsync(enabled: true, batchSize: batchSize);

        // Assert - validation runs before anything leaves the process, so a rejected call cannot leave the
        // backend tombstoning payloads that no job was started to delete.
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        invoker.Methods.Should().BeEmpty();
    }

    [Fact]
    public async Task NonGrpcClient_FailsWithoutAnyRpc()
    {
        // Arrange - the RPC lives on the gRPC client, so any other implementation must be rejected rather than
        // silently no-op'd.
        StubDurableTaskClient client = new();

        // Act
        Func<Task> act = () => client.SetLargePayloadAutoPurgeAsync(enabled: true);

        // Assert
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task EntitiesDisabledClient_FailsBeforeTheBackendIsTouched()
    {
        // Arrange - the worst partial failure this ordering prevents. Discovering the missing entity client
        // after the setting was written would leave the backend tombstoning payloads with nothing running to
        // delete them, so the local prerequisite is resolved first.
        RecordingCallInvoker invoker = new();
        await using GrpcDurableTaskClient client = CreateClient(invoker, enableEntitySupport: false);

        // Act
        Func<Task> act = () => client.SetLargePayloadAutoPurgeAsync(enabled: true);

        // Assert
        await act.Should().ThrowAsync<NotSupportedException>();
        invoker.Methods.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenTheBackendSettingFails_TheJobIsNotSignalled()
    {
        // Arrange
        RecordingCallInvoker invoker = new()
        {
            SetFailure = new RpcException(new Status(StatusCode.Unavailable, "backend is down")),
        };
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        Func<Task> act = () => client.SetLargePayloadAutoPurgeAsync(enabled: true);

        // Assert - the failure is propagated verbatim, not translated into a partial success, and the second
        // operation never runs.
        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unavailable);
        invoker.Methods.Should().Equal("SetLargePayloadAutoPurge");
    }

    [Fact]
    public async Task WhenTheBackendDoesNotImplementTheRpc_TheJobIsNotSignalled()
    {
        // Arrange - an older backend, or one that is not the Durable Task Scheduler at all.
        RecordingCallInvoker invoker = new()
        {
            SetFailure = new RpcException(new Status(StatusCode.Unimplemented, "no such service")),
        };
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        Func<Task> act = () => client.SetLargePayloadAutoPurgeAsync(enabled: true);

        // Assert - mapped exactly as every other public method on the gRPC client maps Unimplemented.
        (await act.Should().ThrowAsync<NotImplementedException>()).Which.Message.Should().Be("no such service");
        invoker.Methods.Should().Equal("SetLargePayloadAutoPurge");
    }

    [Fact]
    public async Task WhenTheBackendSettingIsCancelled_TheJobIsNotSignalled()
    {
        // Arrange
        RecordingCallInvoker invoker = new()
        {
            SetFailure = new RpcException(new Status(StatusCode.Cancelled, "canceled")),
        };
        await using GrpcDurableTaskClient client = CreateClient(invoker);
        using CancellationTokenSource cts = new();
        cts.Cancel();

        // Act
        Func<Task> act = () => client.SetLargePayloadAutoPurgeAsync(enabled: true, cancellationToken: cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        invoker.Methods.Should().Equal("SetLargePayloadAutoPurge");
    }

    [Fact]
    public async Task WhenTheEntitySignalFails_TheSettingIsSurfacedButNotRolledBack()
    {
        // Arrange - the one genuinely partial outcome. Rolling the setting back would be its own operation that
        // can fail in turn, and it would be wrong as often as it was right, because a concurrent caller may
        // have written the value the rollback would undo.
        RecordingCallInvoker invoker = new()
        {
            SignalFailure = new RpcException(new Status(StatusCode.Internal, "entity unreachable")),
        };
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        Func<Task> act = () => client.SetLargePayloadAutoPurgeAsync(enabled: true);

        // Assert - the failure reaches the caller, and no compensating Set follows it. A second Set here would
        // be the rollback the design deliberately does not perform.
        await act.Should().ThrowAsync<RpcException>();
        invoker.Methods.Should().Equal("SetLargePayloadAutoPurge", "SignalEntity");
        invoker.Sets.Should().ContainSingle();
    }

    [Fact]
    public async Task RepeatedIdenticalCalls_IssueTheSameTwoOperationsAgain()
    {
        // Arrange - the retry story. Both operations are idempotent, so a caller that is unsure of the current
        // state, or that is retrying after a partial failure, can simply call again.
        RecordingCallInvoker invoker = new();
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        await client.SetLargePayloadAutoPurgeAsync(enabled: true, batchSize: 250);
        await client.SetLargePayloadAutoPurgeAsync(enabled: true, batchSize: 250);

        // Assert
        invoker.Methods.Should().Equal(
            "SetLargePayloadAutoPurge", "SignalEntity", "SetLargePayloadAutoPurge", "SignalEntity");
        invoker.Sets.Should().HaveCount(2).And.OnlyContain(s => s.Enabled);
        invoker.Signals.Should().HaveCount(2).And.OnlyContain(s => s.Name == "Create" && s.Input == "250");
    }

    [Fact]
    public async Task BothOperationsRideTheClientsConfiguredInterceptorChain()
    {
        // Arrange - the interceptor stands in for the configured cross-cutting chain (authentication is the one
        // that matters in production). If the purge client were built on a raw invoker instead of the client's
        // effective one, the interceptor would see the entity signal and not the setting.
        RecordingCallInvoker invoker = new();
        RecordingInterceptor interceptor = new();
        GrpcDurableTaskClientOptions options = new()
        {
            CallInvoker = invoker,
            EnableEntitySupport = true,
        };
        options.Interceptors.Add(interceptor);
        await using GrpcDurableTaskClient client = new("test", options, NullLogger.Instance);

        // Act
        await client.SetLargePayloadAutoPurgeAsync(enabled: true);

        // Assert
        interceptor.Calls.Should().Contain("SetLargePayloadAutoPurge");
        interceptor.Calls.Should().Contain("SignalEntity");
    }

    [Fact]
    public async Task WhenTheClientRecreatesItsChannel_APreconstructedClientFollowsIt()
    {
        // Arrange - two channels backed by handlers that record which one received a call. The client is
        // constructed ONCE up front, before any recreate, so the property under test is that a client which has
        // already built its purge client routes later calls through the replacement channel rather than the one
        // it happened to see at construction.
        RecordingHandler handlerA = new();
        RecordingHandler handlerB = new();
        using GrpcChannel channelA = GrpcChannel.ForAddress(
            "http://localhost:14101", new GrpcChannelOptions { HttpHandler = handlerA });
        using GrpcChannel channelB = GrpcChannel.ForAddress(
            "http://localhost:14102", new GrpcChannelOptions { HttpHandler = handlerB });

        GrpcDurableTaskClientOptions options = new()
        {
            Channel = channelA,
            EnableEntitySupport = true,
        };

        options.SetChannelRecreator((_, _) => Task.FromResult(channelB));

        await using GrpcDurableTaskClient client = new("test", options, NullLogger.Instance);

        // Act - the client recreates its channel only after a run of consecutive transport failures, so the
        // arrange step has to actually produce that run. Every one of these lands on A, which is what arms the
        // recreate; the final call afterwards then has to land on B.
        for (int i = 0; i < ConsecutiveFailuresBeforeRecreate; i++)
        {
            await SetIgnoringTransportFailureAsync(client);
        }

        await WaitForAsync(() => handlerB.Calls > 0 || handlerA.Calls > ConsecutiveFailuresBeforeRecreate);
        int callsOnABeforeTheLastAttempt = handlerA.Calls;
        await SetIgnoringTransportFailureAsync(client);

        // Assert - B saw the later call, so the already-constructed purge client followed the swap. No second
        // transport was created: both calls went through the client's own channel-recreating invoker.
        handlerA.SetCalls.Should().BeGreaterThan(0);
        handlerB.SetCalls.Should().BeGreaterThan(0);
        handlerA.Calls.Should().Be(callsOnABeforeTheLastAttempt);
    }

    static GrpcDurableTaskClient CreateClient(CallInvoker invoker, bool enableEntitySupport = true)
        => new(
            "test",
            new GrpcDurableTaskClientOptions
            {
                CallInvoker = invoker,
                EnableEntitySupport = enableEntitySupport,
            },
            NullLogger.Instance);

    static async Task SetIgnoringTransportFailureAsync(GrpcDurableTaskClient client)
    {
        // The fake endpoints answer every call with a trailers-only Unavailable, so the call always fails.
        // Which handler recorded it is the signal, not whether it succeeded - but the status is asserted rather
        // than swallowed so an unrelated failure cannot masquerade as the expected transport failure.
        RpcException failure = await Assert.ThrowsAsync<RpcException>(
            () => client.SetLargePayloadAutoPurgeAsync(enabled: true));
        failure.StatusCode.Should().Be(StatusCode.Unavailable);
    }

    static async Task WaitForAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
    }

    /// <summary>
    /// Records the gRPC methods the client issues, in order, and answers each one. Both the purge service and
    /// the sidecar service ride the same invoker, which is exactly what makes their relative order observable.
    /// </summary>
    sealed class RecordingCallInvoker : CallInvoker
    {
        readonly ConcurrentQueue<string> methods = new();
        readonly ConcurrentQueue<LP.SetLargePayloadAutoPurgeRequest> sets = new();
        readonly ConcurrentQueue<P.SignalEntityRequest> signals = new();

        public IReadOnlyList<string> Methods => this.methods.ToArray();

        public IReadOnlyList<LP.SetLargePayloadAutoPurgeRequest> Sets => this.sets.ToArray();

        public IReadOnlyList<P.SignalEntityRequest> Signals => this.signals.ToArray();

        public RpcException? SetFailure { get; init; }

        public RpcException? SignalFailure { get; init; }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            this.methods.Enqueue(method.Name);

            Task<TResponse> response;
            switch (request)
            {
                case LP.SetLargePayloadAutoPurgeRequest set:
                    this.sets.Enqueue(set);
                    response = this.SetFailure is null
                        ? Task.FromResult((TResponse)(object)new LP.SetLargePayloadAutoPurgeResponse())
                        : Task.FromException<TResponse>(this.SetFailure);
                    break;
                case P.SignalEntityRequest signal:
                    this.signals.Enqueue(signal);
                    response = this.SignalFailure is null
                        ? Task.FromResult((TResponse)(object)new P.SignalEntityResponse())
                        : Task.FromException<TResponse>(this.SignalFailure);
                    break;
                default:
                    response = Task.FromException<TResponse>(
                        new RpcException(new Status(StatusCode.Unimplemented, method.Name)));
                    break;
            }

            return new AsyncUnaryCall<TResponse>(
                response,
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { });
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
            => throw new NotSupportedException();

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
            => throw new NotSupportedException();

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options)
            => throw new NotSupportedException();

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Records the gRPC method names that pass through the client's configured interceptor chain.
    /// </summary>
    sealed class RecordingInterceptor : Interceptor
    {
        readonly ConcurrentQueue<string> calls = new();

        public IReadOnlyCollection<string> Calls => this.calls.ToArray();

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        {
            this.calls.Enqueue(context.Method.Name);
            return continuation(request, context);
        }
    }

    /// <summary>
    /// A channel-backing handler that answers every call with a trailers-only
    /// <see cref="StatusCode.Unavailable"/>, so a call can be attributed to one channel without network I/O and
    /// the client's recreate logic classifies the failure the same way every run.
    /// </summary>
    sealed class RecordingHandler : HttpMessageHandler
    {
        int calls;
        int setCalls;

        public int Calls => Volatile.Read(ref this.calls);

        public int SetCalls => Volatile.Read(ref this.setCalls);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this.calls);
            if (request.RequestUri!.AbsolutePath.EndsWith("/SetLargePayloadAutoPurge", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref this.setCalls);
            }

            // Returning a real gRPC status rather than throwing keeps the classification out of the transport
            // library's exception-mapping rules. Ownership of the response transfers to the caller, so it is
            // deliberately not disposed here.
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Version = new Version(2, 0),
                Content = new ByteArrayContent(Array.Empty<byte>()),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            response.Headers.TryAddWithoutValidation("grpc-status", ((int)StatusCode.Unavailable).ToString());
            response.Headers.TryAddWithoutValidation("grpc-message", "This endpoint intentionally never answers.");
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// A non-gRPC client, which the extension must reject before it changes anything.
    /// </summary>
    sealed class StubDurableTaskClient : DurableTaskClient
    {
        public StubDurableTaskClient()
            : base("stub")
        {
        }

        public override ValueTask DisposeAsync() => default;

        public override Task<string> ScheduleNewOrchestrationInstanceAsync(
            TaskName orchestratorName,
            object? input = null,
            StartOrchestrationOptions? options = null,
            CancellationToken cancellation = default)
            => throw new NotSupportedException();

        public override Task RaiseEventAsync(
            string instanceId, string eventName, object? eventPayload = null, CancellationToken cancellation = default)
            => throw new NotSupportedException();

        public override Task TerminateInstanceAsync(
            string instanceId, TerminateInstanceOptions? options = null, CancellationToken cancellation = default)
            => throw new NotSupportedException();

        public override Task SuspendInstanceAsync(
            string instanceId, string? reason = null, CancellationToken cancellation = default)
            => throw new NotSupportedException();

        public override Task ResumeInstanceAsync(
            string instanceId, string? reason = null, CancellationToken cancellation = default)
            => throw new NotSupportedException();

        public override Task<OrchestrationMetadata?> GetInstancesAsync(
            string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
            => throw new NotSupportedException();

        public override AsyncPageable<OrchestrationMetadata> GetAllInstancesAsync(OrchestrationQuery? filter = null)
            => throw new NotSupportedException();

        public override Task<OrchestrationMetadata> WaitForInstanceStartAsync(
            string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
            => throw new NotSupportedException();

        public override Task<OrchestrationMetadata> WaitForInstanceCompletionAsync(
            string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
            => throw new NotSupportedException();

        public override Task<PurgeResult> PurgeInstanceAsync(
            string instanceId, PurgeInstanceOptions? options = null, CancellationToken cancellation = default)
            => throw new NotSupportedException();

        public override Task<PurgeResult> PurgeAllInstancesAsync(
            PurgeInstancesFilter filter, PurgeInstanceOptions? options = null, CancellationToken cancellation = default)
            => throw new NotSupportedException();
    }
}
