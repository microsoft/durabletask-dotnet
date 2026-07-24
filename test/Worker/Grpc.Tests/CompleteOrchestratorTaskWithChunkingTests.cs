// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using FluentAssertions;
using Grpc.Core;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

/// <summary>
/// Focused unit tests for <c>Processor.CompleteOrchestratorTaskWithChunkingAsync</c>, covering the
/// size-boundary decisions, the <see cref="P.WorkerCapability.LargePayloads"/> capability combinations,
/// oversized-single-action failure behavior, chunk wire-compatibility, and retry behavior. These tests
/// guard against regressions in the optimization that avoids recalculating protobuf action sizes
/// (see https://github.com/microsoft/durabletask-dotnet/issues/773).
/// </summary>
public class CompleteOrchestratorTaskWithChunkingTests
{
    [Fact]
    public async Task ResponseFitsExactlyAtLimit_SendsOriginalResponseDirectly_NoChunking()
    {
        // Arrange
        P.OrchestratorResponse response = BuildResponse(
            "instance-1",
            BuildScheduleTaskAction(0, 16),
            BuildScheduleTaskAction(1, 16));
        int maxChunkBytes = response.CalculateSize();

        using Fixture fixture = Fixture.Create();

        // Act
        await fixture.InvokeAsync(response, maxChunkBytes);

        // Assert - the exact same response instance is sent, unmodified, in a single call.
        fixture.Sent.Should().HaveCount(1);
        fixture.Sent[0].Should().BeSameAs(response);
#pragma warning disable CS0612 // IsPartial/ChunkIndex are deprecated but still part of the wire contract.
        fixture.Sent[0].IsPartial.Should().BeFalse();
        fixture.Sent[0].ChunkIndex.Should().BeNull();
#pragma warning restore CS0612
    }

    [Fact]
    public async Task ResponseOneByteOverLimit_ActionsStillFitIndividually_ProducesSingleNonPartialChunk()
    {
        // Arrange - the whole response is one byte too large, but rebuilding a chunk from the
        // (already fitting) individual actions still yields just one, non-partial chunk. This
        // exercises the chunking code path (not the direct-send fast path) while confirming the
        // resulting chunk still preserves all wire-compatibility fields correctly.
        P.OrchestratorResponse response = BuildResponse(
            "instance-2",
            BuildScheduleTaskAction(0, 16),
            BuildScheduleTaskAction(1, 16));
        response.CustomStatus = "status";
        response.OrchestrationTraceContext = new P.OrchestrationTraceContext { SpanID = "span-1" };
        int exactSize = response.CalculateSize();

        using Fixture fixture = Fixture.Create();

        // Act
        await fixture.InvokeAsync(response, exactSize - 1);

        // Assert
        fixture.Sent.Should().HaveCount(1);
        P.OrchestratorResponse sent = fixture.Sent[0];
        sent.Should().NotBeSameAs(response); // Went through the chunking construction path.
        sent.InstanceId.Should().Be(response.InstanceId);
        sent.CompletionToken.Should().Be(response.CompletionToken);
        sent.CustomStatus.Should().Be(response.CustomStatus);
        sent.OrchestrationTraceContext.SpanID.Should().Be(response.OrchestrationTraceContext.SpanID);
        sent.NumEventsProcessed.Should().BeNull();
        sent.Actions.Should().HaveCount(2);
#pragma warning disable CS0612
        sent.IsPartial.Should().BeFalse();
        sent.ChunkIndex.Should().BeNull();
#pragma warning restore CS0612
    }

    [Fact]
    public async Task ActionExactlyFillsRemainingChunkSpace_IsIncludedInSameChunk()
    {
        // Arrange - two actions whose combined size exactly equals maxChunkBytes. The boundary
        // condition in TryAddAction (currentSize + actionSize > maxChunkBytes) must treat "equal"
        // as fitting, so both actions land in the same, single, non-partial chunk.
        P.OrchestratorAction action0 = BuildScheduleTaskAction(0, 16);
        P.OrchestratorAction action1 = BuildScheduleTaskAction(1, 32);
        int size0 = action0.CalculateSize();
        int size1 = action1.CalculateSize();

        P.OrchestratorResponse response = BuildResponse("instance-3", action0, action1);
        using Fixture fixture = Fixture.Create();

        // Act
        await fixture.InvokeAsync(response, size0 + size1);

        // Assert
        fixture.Sent.Should().HaveCount(1);
        fixture.Sent[0].Actions.Should().HaveCount(2);
        fixture.Sent[0].Actions[0].Id.Should().Be(0);
        fixture.Sent[0].Actions[1].Id.Should().Be(1);
#pragma warning disable CS0612
        fixture.Sent[0].IsPartial.Should().BeFalse();
#pragma warning restore CS0612
    }

    [Fact]
    public async Task ActionExceedsRemainingChunkSpaceByOneByte_IsMovedToNextChunk()
    {
        // Arrange - same two actions, but maxChunkBytes is one byte less than their combined size.
        // The second action must now overflow into its own, second chunk.
        P.OrchestratorAction action0 = BuildScheduleTaskAction(0, 16);
        P.OrchestratorAction action1 = BuildScheduleTaskAction(1, 32);
        int size0 = action0.CalculateSize();
        int size1 = action1.CalculateSize();

        P.OrchestratorResponse response = BuildResponse("instance-4", action0, action1);
        using Fixture fixture = Fixture.Create();

        // Act
        await fixture.InvokeAsync(response, size0 + size1 - 1);

        // Assert
        fixture.Sent.Should().HaveCount(2);
        fixture.Sent[0].Actions.Should().ContainSingle(a => a.Id == 0);
        fixture.Sent[1].Actions.Should().ContainSingle(a => a.Id == 1);
#pragma warning disable CS0612
        fixture.Sent[0].IsPartial.Should().BeTrue();
        fixture.Sent[0].ChunkIndex.Should().Be(0);
        fixture.Sent[1].IsPartial.Should().BeFalse();
        fixture.Sent[1].ChunkIndex.Should().Be(1);
#pragma warning restore CS0612
    }

    [Fact]
    public async Task NoLargePayloadsCapability_SingleOversizedAction_ReturnsFailedCompletionResponse()
    {
        // Arrange - one small action and one large action that alone exceeds maxChunkBytes. Without
        // the LargePayloads capability, this must fail the orchestration instead of sending the
        // oversized action.
        P.OrchestratorAction small = BuildScheduleTaskAction(0, 16);
        P.OrchestratorAction large = BuildScheduleTaskAction(1, 2048);
        int largeSize = large.CalculateSize();
        int maxChunkBytes = largeSize - 1;

        P.OrchestratorResponse response = BuildResponse("instance-5", small, large);
        response.OrchestrationTraceContext = new P.OrchestrationTraceContext { SpanID = "span-5" };
        using Fixture fixture = Fixture.Create(largePayloads: false);

        // Act
        await fixture.InvokeAsync(response, maxChunkBytes);

        // Assert
        fixture.Sent.Should().HaveCount(1);
        P.OrchestratorResponse failure = fixture.Sent[0];
        failure.InstanceId.Should().Be(response.InstanceId);
        failure.CompletionToken.Should().Be(response.CompletionToken);
        failure.OrchestrationTraceContext.SpanID.Should().Be("span-5");
        failure.Actions.Should().HaveCount(1);
        P.OrchestratorAction failureAction = failure.Actions[0];
        failureAction.CompleteOrchestration.Should().NotBeNull();
        failureAction.CompleteOrchestration.OrchestrationStatus.Should().Be(P.OrchestrationStatus.Failed);
        failureAction.CompleteOrchestration.FailureDetails.IsNonRetriable.Should().BeTrue();
        failureAction.CompleteOrchestration.FailureDetails.ErrorType.Should().Be(typeof(InvalidOperationException).FullName);
        string expectedMessage = $"A single orchestrator action of type ScheduleTask with id 1 " +
            $"exceeds the {maxChunkBytes / 1024.0 / 1024.0:F2}MB limit: {largeSize / 1024.0 / 1024.0:F2}MB. " +
            "Enable large-payload externalization to Azure Blob Storage to support oversized actions.";
        failureAction.CompleteOrchestration.FailureDetails.ErrorMessage.Should().Be(expectedMessage);
    }

    [Fact]
    public async Task NoLargePayloadsCapability_FirstActionOversized_FailsOnFirstOffender_DoesNotEvaluateLaterActions()
    {
        // Arrange - regression coverage for a fail-fast ordering bug: without the LargePayloads
        // capability, validation must stop at the *first* oversized action instead of first sizing
        // the whole response and/or every action. Action id=0 is oversized, and several later
        // actions (ids 1-3) are ALSO individually oversized with distinguishable ids. If the
        // algorithm regresses back to "size everything, then scan for a failure", it could still
        // produce *a* failure, but this asserts it fails on id=0 specifically - proving iteration
        // stopped at the very first offender rather than continuing to size/scan later actions.
        P.OrchestratorAction action0 = BuildScheduleTaskAction(0, 2048); // oversized - first, must win
        P.OrchestratorAction action1 = BuildScheduleTaskAction(1, 2048); // also oversized
        P.OrchestratorAction action2 = BuildScheduleTaskAction(2, 2048); // also oversized
        P.OrchestratorAction action3 = BuildScheduleTaskAction(3, 2048); // also oversized
        int maxChunkBytes = action0.CalculateSize() - 1;

        P.OrchestratorResponse response = BuildResponse("instance-9", action0, action1, action2, action3);
        using Fixture fixture = Fixture.Create(largePayloads: false);

        // Act
        await fixture.InvokeAsync(response, maxChunkBytes);

        // Assert - exactly one response was sent (no partial chunks for the other oversized
        // actions), and it is a failure referencing id=0.
        fixture.Sent.Should().HaveCount(1);
        P.OrchestratorResponse failure = fixture.Sent[0];
        failure.Actions.Should().HaveCount(1);
        P.OrchestratorAction failureAction = failure.Actions[0];
        failureAction.CompleteOrchestration.Should().NotBeNull();
        failureAction.CompleteOrchestration.OrchestrationStatus.Should().Be(P.OrchestrationStatus.Failed);
        failureAction.CompleteOrchestration.FailureDetails.ErrorMessage.Should().Contain("with id 0 ");
        failureAction.CompleteOrchestration.FailureDetails.ErrorMessage.Should().NotContain("with id 1 ");
        failureAction.CompleteOrchestration.FailureDetails.ErrorMessage.Should().NotContain("with id 2 ");
        failureAction.CompleteOrchestration.FailureDetails.ErrorMessage.Should().NotContain("with id 3 ");
    }

    [Fact]
    public async Task NoLargePayloadsCapability_FirstActionOversized_ShortCircuitsBeforeSizingLaterActions()
    {
        // Arrange - a stronger, timing-based guard for the same regression as above. Action id=0 is
        // oversized (tiny payload, cheap to size); actions after it each carry a large *shared*
        // string reference (so memory stays low - only one big string is allocated - while
        // CalculateSize() must still independently re-walk it on every call). Calibration measured
        // ~1000ms to size 200 such actions vs. ~4ms to size a single one, so a fail-fast
        // implementation (sizing only id=0 before returning) should complete orders of magnitude
        // faster than a regressed implementation that sizes the whole response and/or every action
        // first. The assertion uses a very generous threshold to avoid CI flakiness while still
        // clearly separating the two behaviors.
        string bigSharedInput = new('x', 40_000_000);
        P.OrchestratorAction action0 = BuildScheduleTaskAction(0, 16); // small, oversized only relative to maxChunkBytes below
        int maxChunkBytes = action0.CalculateSize() - 1;

        List<P.OrchestratorAction> actions = new() { action0 };
        for (int i = 1; i <= 200; i++)
        {
            actions.Add(new P.OrchestratorAction
            {
                Id = i,
                ScheduleTask = new P.ScheduleTaskAction { Name = "Echo", Input = bigSharedInput },
            });
        }

        P.OrchestratorResponse response = BuildResponse("instance-10", actions.ToArray());
        using Fixture fixture = Fixture.Create(largePayloads: false);

        // Act
        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await fixture.InvokeAsync(response, maxChunkBytes);
        stopwatch.Stop();

        // Assert - failed fast on id=0 well before the ~1000ms it would take to size the 200
        // large later actions.
        fixture.Sent.Should().HaveCount(1);
        fixture.Sent[0].Actions[0].CompleteOrchestration.FailureDetails.ErrorMessage.Should().Contain("with id 0 ");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(500);
    }

    [Fact]
    public async Task LargePayloadsCapability_SingleOversizedAction_IsSentInsteadOfFailing()
    {
        // Arrange - same oversized action, but with LargePayloads capability announced. The action
        // must be allowed through (as its own chunk) rather than failing the orchestration.
        P.OrchestratorAction small = BuildScheduleTaskAction(0, 16);
        P.OrchestratorAction large = BuildScheduleTaskAction(1, 2048);
        int largeSize = large.CalculateSize();
        int maxChunkBytes = largeSize - 1;

        P.OrchestratorResponse response = BuildResponse("instance-6", small, large);
        using Fixture fixture = Fixture.Create(largePayloads: true);

        // Act
        await fixture.InvokeAsync(response, maxChunkBytes);

        // Assert - both actions are sent (each too big to share a chunk with the other), and
        // neither call carries a failed CompleteOrchestration action.
        fixture.Sent.Should().HaveCountGreaterOrEqualTo(1);
        fixture.Sent.SelectMany(r => r.Actions).Should().Contain(a => a.Id == 0);
        fixture.Sent.SelectMany(r => r.Actions).Should().Contain(a => a.Id == 1);
        fixture.Sent.SelectMany(r => r.Actions).Should().NotContain(a => a.OrchestratorActionTypeCase == P.OrchestratorAction.OrchestratorActionTypeOneofCase.CompleteOrchestration);
    }

    [Fact]
    public async Task MultiChunkResponse_PreservesWireCompatibilityFieldsAcrossChunks()
    {
        // Arrange - three actions that must span three separate chunks, verifying InstanceId,
        // CompletionToken, and CustomStatus repeat every chunk; OrchestrationTraceContext is only
        // set on the first chunk; NumEventsProcessed is null on the first chunk and 0 afterward;
        // RequiresHistory is preserved; and chunk indices/IsPartial sequence correctly.
        P.OrchestratorAction action0 = BuildScheduleTaskAction(0, 16);
        P.OrchestratorAction action1 = BuildScheduleTaskAction(1, 16);
        P.OrchestratorAction action2 = BuildScheduleTaskAction(2, 16);
        int maxChunkBytes = Math.Max(action0.CalculateSize(), Math.Max(action1.CalculateSize(), action2.CalculateSize()));

        P.OrchestratorResponse response = BuildResponse("instance-7", action0, action1, action2);
        response.CustomStatus = "custom-status";
        response.RequiresHistory = true;
        response.OrchestrationTraceContext = new P.OrchestrationTraceContext { SpanID = "span-7" };

        using Fixture fixture = Fixture.Create();

        // Act
        await fixture.InvokeAsync(response, maxChunkBytes);

        // Assert
        fixture.Sent.Should().HaveCount(3);
        for (int i = 0; i < fixture.Sent.Count; i++)
        {
            P.OrchestratorResponse chunk = fixture.Sent[i];
            chunk.InstanceId.Should().Be("instance-7");
            chunk.CompletionToken.Should().Be(response.CompletionToken);
            chunk.CustomStatus.Should().Be("custom-status");
            chunk.RequiresHistory.Should().BeTrue();
#pragma warning disable CS0612
            chunk.ChunkIndex.Should().Be(i);
            chunk.IsPartial.Should().Be(i < fixture.Sent.Count - 1);
#pragma warning restore CS0612

            if (i == 0)
            {
                chunk.NumEventsProcessed.Should().BeNull();
                chunk.OrchestrationTraceContext.Should().NotBeNull();
                chunk.OrchestrationTraceContext.SpanID.Should().Be("span-7");
            }
            else
            {
                chunk.NumEventsProcessed.Should().Be(0);
                chunk.OrchestrationTraceContext.Should().BeNull();
            }
        }

        // All three actions were sent, in order, across the chunks, with none duplicated or dropped.
        fixture.Sent.SelectMany(r => r.Actions).Select(a => a.Id).Should().Equal(0, 1, 2);
    }

    [Fact]
    public async Task TransientRpcError_DuringSend_RetriesAndEventuallySucceeds()
    {
        // Arrange - a response that fits in a single chunk (fast path), whose first send attempt
        // fails with a transient gRPC error. The method must still rely on ExecuteWithRetryAsync to
        // retry the same request and eventually succeed.
        P.OrchestratorResponse response = BuildResponse("instance-8", BuildScheduleTaskAction(0, 16));
        int maxChunkBytes = response.CalculateSize();

        using Fixture fixture = Fixture.Create(transientRetryBackoffBase: TimeSpan.FromMilliseconds(1));
        fixture.FailNextAttempts(1);

        // Act
        await fixture.InvokeAsync(response, maxChunkBytes);

        // Assert
        fixture.AttemptCount.Should().Be(2);
        fixture.Sent.Should().HaveCount(1);
        fixture.Sent[0].Should().BeSameAs(response);
    }

    static P.OrchestratorResponse BuildResponse(string instanceId, params P.OrchestratorAction[] actions)
    {
        P.OrchestratorResponse response = new()
        {
            InstanceId = instanceId,
            CompletionToken = Guid.NewGuid().ToString("N"),
        };
        response.Actions.AddRange(actions);
        return response;
    }

    static P.OrchestratorAction BuildScheduleTaskAction(int id, int payloadBytes)
    {
        return new P.OrchestratorAction
        {
            Id = id,
            ScheduleTask = new P.ScheduleTaskAction
            {
                Name = "Echo",
                Input = new string('x', payloadBytes),
            },
        };
    }

    /// <summary>
    /// Test fixture that constructs a real <c>GrpcDurableTaskWorker.Processor</c> via reflection (it
    /// is a private nested type) wired to a strictly-mocked gRPC client, and exposes a helper to
    /// invoke the private <c>CompleteOrchestratorTaskWithChunkingAsync</c> method directly.
    /// </summary>
    sealed class Fixture : IDisposable
    {
        static readonly MethodInfo Method = FindMethod();

        readonly object processor;
        readonly object gate = new();
        int attemptsToFail;

        Fixture(object processor, Mock<P.TaskHubSidecarService.TaskHubSidecarServiceClient> clientMock)
        {
            this.processor = processor;
            this.ClientMock = clientMock;
        }

        public Mock<P.TaskHubSidecarService.TaskHubSidecarServiceClient> ClientMock { get; }

        public List<P.OrchestratorResponse> Sent { get; } = new();

        public int AttemptCount { get; private set; }

        public static Fixture Create(
            bool largePayloads = false,
            TimeSpan? transientRetryBackoffBase = null)
        {
            GrpcDurableTaskWorkerOptions grpcOptionsValue = new();
            if (largePayloads)
            {
                grpcOptionsValue.Capabilities.Add(P.WorkerCapability.LargePayloads);
            }

            if (transientRetryBackoffBase.HasValue)
            {
                grpcOptionsValue.Internal.TransientRetryBackoffBase = transientRetryBackoffBase.Value;
            }

            OptionsMonitorStub<GrpcDurableTaskWorkerOptions> grpcOptions = new(grpcOptionsValue);
            OptionsMonitorStub<DurableTaskWorkerOptions> workerOptions = new(new DurableTaskWorkerOptions());
            Mock<IDurableTaskFactory> factoryMock = new(MockBehavior.Strict);

            GrpcDurableTaskWorker worker = new(
                name: "Test",
                factory: factoryMock.Object,
                grpcOptions: grpcOptions,
                workerOptions: workerOptions,
                services: Mock.Of<IServiceProvider>(),
                loggerFactory: NullLoggerFactory.Instance,
                orchestrationFilter: null,
                exceptionPropertiesProvider: null);

            CallInvoker callInvoker = Mock.Of<CallInvoker>();
            Mock<P.TaskHubSidecarService.TaskHubSidecarServiceClient> clientMock = new(
                MockBehavior.Strict, new object[] { callInvoker });

            Type processorType = typeof(GrpcDurableTaskWorker).GetNestedType("Processor", BindingFlags.NonPublic)!;
            object processorInstance = Activator.CreateInstance(
                processorType,
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                args: new object?[] { worker, clientMock.Object, null, null },
                culture: null)!;

            Fixture fixture = new(processorInstance, clientMock);

            clientMock
                .Setup(c => c.CompleteOrchestratorTaskAsync(
                    It.IsAny<P.OrchestratorResponse>(),
                    It.IsAny<Metadata>(),
                    It.IsAny<DateTime?>(),
                    It.IsAny<CancellationToken>()))
                .Returns((P.OrchestratorResponse r, Metadata h, DateTime? d, CancellationToken ct) =>
                    fixture.HandleSend(r));

            return fixture;
        }

        /// <summary>
        /// Configures the next <paramref name="count"/> send attempts (across all chunks) to fail
        /// with a transient (Unavailable) gRPC error before subsequent attempts succeed.
        /// </summary>
        public void FailNextAttempts(int count)
        {
            lock (this.gate)
            {
                this.attemptsToFail = count;
            }
        }

        public Task InvokeAsync(P.OrchestratorResponse response, int maxChunkBytes, CancellationToken cancellationToken = default)
        {
            return (Task)Method.Invoke(this.processor, new object?[] { response, maxChunkBytes, cancellationToken })!;
        }

        public void Dispose()
        {
        }

        AsyncUnaryCall<P.CompleteTaskResponse> HandleSend(P.OrchestratorResponse response)
        {
            bool shouldFail;
            lock (this.gate)
            {
                this.AttemptCount++;
                shouldFail = this.attemptsToFail > 0;
                if (shouldFail)
                {
                    this.attemptsToFail--;
                }
            }

            if (shouldFail)
            {
                return RpcExceptionAsyncUnaryCall<P.CompleteTaskResponse>(StatusCode.Unavailable);
            }

            this.Sent.Add(response);
            return CompletedAsyncUnaryCall(new P.CompleteTaskResponse());
        }

        static MethodInfo FindMethod()
        {
            Type processorType = typeof(GrpcDurableTaskWorker).GetNestedType("Processor", BindingFlags.NonPublic)!;
            return processorType.GetMethod("CompleteOrchestratorTaskWithChunkingAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        }

        static AsyncUnaryCall<T> CompletedAsyncUnaryCall<T>(T response)
        {
            Task<T> respTask = Task.FromResult(response);
            return new AsyncUnaryCall<T>(
                respTask,
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, string.Empty),
                () => new Metadata(),
                () => { });
        }

        static AsyncUnaryCall<T> RpcExceptionAsyncUnaryCall<T>(StatusCode statusCode, string detail = "transient error")
        {
            RpcException ex = new(new Status(statusCode, detail));
            Task<T> respTask = Task.FromException<T>(ex);
            return new AsyncUnaryCall<T>(
                respTask,
                Task.FromResult(new Metadata()),
                () => new Status(statusCode, detail),
                () => new Metadata(),
                () => { });
        }

        sealed class OptionsMonitorStub<T> : IOptionsMonitor<T>
            where T : class, new()
        {
            readonly T value;

            public OptionsMonitorStub(T value) => this.value = value;

            public T CurrentValue => this.value;

            public T Get(string? name) => this.value;

            public IDisposable OnChange(Action<T, string?> listener) => NullDisposable.Instance;

            sealed class NullDisposable : IDisposable
            {
                public static readonly NullDisposable Instance = new();

                public void Dispose()
                {
                }
            }
        }
    }
}
