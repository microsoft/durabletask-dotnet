// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using DurableTask.Core;
using DurableTask.Core.Command;
using DurableTask.Core.History;
using Grpc.Core;
using Microsoft.DurableTask.Testing.Sidecar;
using Microsoft.DurableTask.Testing.Sidecar.Dispatcher;
using Microsoft.DurableTask.Testing.Sidecar.Grpc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;
using P = Microsoft.DurableTask.Protobuf;

namespace InProcessTestHost.Tests;

/// <summary>
/// Tests completion ownership of individual work-item deliveries.
/// </summary>
public class WorkItemCompletionTests
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Repeated deliveries of the same logical work item have distinct nonempty tokens.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispatch_AssignsUniqueDeliveryTokensAsync(bool activity)
    {
        // Arrange
        await using ServerSession session = new();
        Task firstExecution = session.StartExecution(activity);
        P.WorkItem first = await session.ReadAsync();
        await CompleteAsync(session.Server, first);
        await firstExecution.WaitAsync(Timeout);

        // Act
        Task nextExecution = session.StartExecution(activity);
        P.WorkItem next = await session.ReadAsync();

        // Assert
        Assert.NotEmpty(first.CompletionToken);
        Assert.NotEmpty(next.CompletionToken);
        Assert.NotEqual(first.CompletionToken, next.CompletionToken);
        Assert.False(nextExecution.IsCompleted);
        await CompleteAsync(session.Server, next);
        await nextExecution.WaitAsync(Timeout);
    }

    /// <summary>
    /// Duplicate completion cannot settle a replacement or an unrelated delivery.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteWorkItem_StaleTokenDoesNotSettleReplacementAsync(bool activity)
    {
        // Arrange
        await using ServerSession session = new();
        Task firstExecution = session.StartExecution(activity);
        P.WorkItem first = await session.ReadAsync();
        await CompleteAsync(session.Server, first);
        await firstExecution.WaitAsync(Timeout);
        Task replacementExecution = session.StartExecution(activity);
        P.WorkItem replacement = await session.ReadAsync();
        Task otherExecution = session.StartExecution(activity, "other");
        P.WorkItem other = await session.ReadAsync();

        // Act
        RpcException duplicate = await Assert.ThrowsAsync<RpcException>(() => CompleteAsync(session.Server, first));

        // Assert
        Assert.Equal(StatusCode.NotFound, duplicate.StatusCode);
        Assert.False(replacementExecution.IsCompleted);
        Assert.False(otherExecution.IsCompleted);
        await CompleteAsync(session.Server, replacement);
        await CompleteAsync(session.Server, other);
        await Task.WhenAll(replacementExecution, otherExecution).WaitAsync(Timeout);
    }

    /// <summary>
    /// Missing, unknown, and wrong-kind completion tokens leave active deliveries untouched.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteWorkItem_InvalidTokensAreRejectedAsync(bool activity)
    {
        // Arrange
        await using ServerSession session = new();
        Task execution = session.StartExecution(activity);
        P.WorkItem delivery = await session.ReadAsync();
        Task otherExecution = session.StartExecution(!activity, "other");
        P.WorkItem other = await session.ReadAsync();
        P.WorkItem invalid = delivery.Clone();

        // Act
        invalid.CompletionToken = string.Empty;
        RpcException empty = await Assert.ThrowsAsync<RpcException>(() => CompleteAsync(session.Server, invalid));
        invalid.CompletionToken = "unknown";
        RpcException unknown = await Assert.ThrowsAsync<RpcException>(() => CompleteAsync(session.Server, invalid));
        invalid.CompletionToken = other.CompletionToken;
        RpcException wrongKind = await Assert.ThrowsAsync<RpcException>(() => CompleteAsync(session.Server, invalid));

        // Assert
        Assert.Equal(StatusCode.InvalidArgument, empty.StatusCode);
        Assert.Equal(StatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(StatusCode.NotFound, wrongKind.StatusCode);
        Assert.False(execution.IsCompleted);
        Assert.False(otherExecution.IsCompleted);
        await CompleteAsync(session.Server, delivery);
        await CompleteAsync(session.Server, other);
        await Task.WhenAll(execution, otherExecution).WaitAsync(Timeout);
    }

    /// <summary>
    /// A mismatched logical identity does not consume a valid completion token.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteWorkItem_InvalidIdentityDoesNotRemoveDeliveryAsync(bool activity)
    {
        // Arrange
        await using ServerSession session = new();
        Task execution = session.StartExecution(activity);
        P.WorkItem delivery = await session.ReadAsync();
        P.WorkItem invalid = delivery.Clone();
        if (activity)
        {
            invalid.ActivityRequest.OrchestrationInstance.InstanceId = "other";
        }
        else
        {
            invalid.OrchestratorRequest.InstanceId = "other";
        }

        // Act
        RpcException mismatch = await Assert.ThrowsAsync<RpcException>(() => CompleteAsync(session.Server, invalid));

        // Assert
        Assert.Equal(StatusCode.InvalidArgument, mismatch.StatusCode);
        Assert.False(execution.IsCompleted);
        if (activity)
        {
            invalid = delivery.Clone();
            invalid.ActivityRequest.TaskId++;
            RpcException wrongTaskId = await Assert.ThrowsAsync<RpcException>(() => CompleteAsync(session.Server, invalid));
            Assert.Equal(StatusCode.InvalidArgument, wrongTaskId.StatusCode);
            Assert.False(execution.IsCompleted);
        }

        await CompleteAsync(session.Server, delivery);
        await execution.WaitAsync(Timeout);
    }

    /// <summary>
    /// Concurrent final responses have exactly one winner and preserve that winner's result.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteWorkItem_OnlyOneResponseClaimsDeliveryAsync(bool activity)
    {
        // Arrange
        await using ServerSession session = new();
        Task execution = session.StartExecution(activity);
        P.WorkItem delivery = await session.ReadAsync();
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<StatusCode> first = Task.Run(() => CompleteAfterSignalAsync(start.Task, () => CompleteAsync(session.Server, delivery, "\"first\"")));
        Task<StatusCode> second = Task.Run(() => CompleteAfterSignalAsync(start.Task, () => CompleteAsync(session.Server, delivery, "\"second\"")));

        // Act
        start.SetResult();
        StatusCode[] outcomes = await Task.WhenAll(first, second).WaitAsync(Timeout);
        await execution.WaitAsync(Timeout);

        // Assert
        Assert.Single(outcomes, status => status == StatusCode.OK);
        Assert.Single(outcomes, status => status == StatusCode.NotFound);
        string expected = outcomes[0] == StatusCode.OK ? "\"first\"" : "\"second\"";
        if (activity)
        {
            ActivityExecutionResult result = await (Task<ActivityExecutionResult>)execution;
            Assert.Equal(expected, Assert.IsType<TaskCompletedEvent>(result.ResponseEvent).Result);
        }
        else
        {
            GrpcOrchestratorExecutionResult result = await (Task<GrpcOrchestratorExecutionResult>)execution;
            Assert.Equal(expected, result.CustomStatus);
        }
    }

    /// <summary>
    /// Activity completion preserves successful results and application failures.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteActivityTask_PreservesResultAsync(bool failed)
    {
        // Arrange
        await using ServerSession session = new();
        Task execution = session.StartExecution(activity: true);
        P.WorkItem delivery = await session.ReadAsync();

        // Act
        await session.Server.CompleteActivityTask(new()
        {
            InstanceId = delivery.ActivityRequest.OrchestrationInstance.InstanceId,
            TaskId = delivery.ActivityRequest.TaskId,
            CompletionToken = delivery.CompletionToken,
            Result = "\"result\"",
            FailureDetails = failed ? new() { ErrorType = "ExpectedFailure", ErrorMessage = "failed" } : null,
        }, CreateContext());
        ActivityExecutionResult result = await ((Task<ActivityExecutionResult>)execution).WaitAsync(Timeout);

        // Assert
        if (failed)
        {
            TaskFailedEvent failure = Assert.IsType<TaskFailedEvent>(result.ResponseEvent);
            Assert.Equal(delivery.ActivityRequest.TaskId, failure.TaskScheduledId);
            Assert.Equal("ExpectedFailure", failure.FailureDetails?.ErrorType);
        }
        else
        {
            TaskCompletedEvent completed = Assert.IsType<TaskCompletedEvent>(result.ResponseEvent);
            Assert.Equal(delivery.ActivityRequest.TaskId, completed.TaskScheduledId);
            Assert.Equal("\"result\"", completed.Result);
        }
    }

    /// <summary>
    /// Unsupported response fragments explicitly fail their delivery and release its replay snapshot.
    /// </summary>
    [Theory]
    [InlineData(true, null)]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    public async Task CompleteOrchestrator_RejectsChunkedResponsesWithFailedResultAsync(bool partial, int? chunkIndex)
    {
        // Arrange
        await using ServerSession session = new();
        Task execution = session.StartExecution(activity: false, historyPayload: 'x');
        P.WorkItem delivery = await session.ReadAsync();
#pragma warning disable CS0612 // Exercise rejection of the deprecated chunk fields.
        P.OrchestratorResponse fragment = new()
        {
            InstanceId = delivery.OrchestratorRequest.InstanceId,
            CompletionToken = delivery.CompletionToken,
            IsPartial = partial,
            ChunkIndex = chunkIndex,
            Actions = { new P.OrchestratorAction { Id = 0, ScheduleTask = new() { Name = "Rejected" } } },
        };
#pragma warning restore CS0612

        // Act
        RpcException unsupported = await Assert.ThrowsAsync<RpcException>(() =>
            session.Server.CompleteOrchestratorTask(fragment, CreateContext()));

        // Assert
        Assert.Equal(StatusCode.InvalidArgument, unsupported.StatusCode);
        Assert.Contains("Chunked orchestrator completion is not supported", unsupported.Status.Detail);
        GrpcOrchestratorExecutionResult result = await ((Task<GrpcOrchestratorExecutionResult>)execution).WaitAsync(Timeout);
        OrchestrationCompleteOrchestratorAction failure = Assert.IsType<OrchestrationCompleteOrchestratorAction>(Assert.Single(result.Actions));
        Assert.Equal(OrchestrationStatus.Failed, failure.OrchestrationStatus);
        Assert.NotNull(failure.FailureDetails);
        Assert.Equal(typeof(NotSupportedException).FullName, failure.FailureDetails.ErrorType);
        Assert.Equal(unsupported.Status.Detail, failure.FailureDetails.ErrorMessage);
        Assert.True(failure.FailureDetails.IsNonRetriable);
        Assert.Empty(WorkerHistorySnapshotTestHelpers.GetSnapshots(session.Server));
        RpcException duplicate = await Assert.ThrowsAsync<RpcException>(() => CompleteAsync(session.Server, delivery));
        Assert.Equal(StatusCode.NotFound, duplicate.StatusCode);
    }

    /// <summary>
    /// Invalid chunk-request tokens and identities cannot fail a valid delivery.
    /// </summary>
    [Theory]
    [InlineData("missing", StatusCode.InvalidArgument)]
    [InlineData("unknown", StatusCode.NotFound)]
    [InlineData("activity", StatusCode.NotFound)]
    [InlineData("instance", StatusCode.InvalidArgument)]
    public async Task CompleteOrchestrator_InvalidChunkRequestsDoNotSettleDeliveryAsync(string invalid, StatusCode expected)
    {
        // Arrange
        await using ServerSession session = new();
        Task execution = session.StartExecution(activity: false, historyPayload: 'x');
        P.WorkItem delivery = await session.ReadAsync();
        Task otherExecution = session.StartExecution(activity: true, "other");
        P.WorkItem other = await session.ReadAsync();
        List<P.HistoryEvent> snapshot = WorkerHistorySnapshotTestHelpers.GetSnapshots(session.Server)["instance"];
#pragma warning disable CS0612 // Validate ownership before rejecting deprecated chunk fields.
        P.OrchestratorResponse request = new()
        {
            InstanceId = invalid == "instance" ? "other" : delivery.OrchestratorRequest.InstanceId,
            CompletionToken = invalid switch
            {
                "missing" => string.Empty,
                "unknown" => "unknown",
                "activity" => other.CompletionToken,
                _ => delivery.CompletionToken,
            },
            IsPartial = true,
            ChunkIndex = 0,
        };
#pragma warning restore CS0612

        // Act
        RpcException rejected = await Assert.ThrowsAsync<RpcException>(() =>
            session.Server.CompleteOrchestratorTask(request, CreateContext()));

        // Assert
        Assert.Equal(expected, rejected.StatusCode);
        Assert.False(execution.IsCompleted);
        Assert.False(otherExecution.IsCompleted);
        Assert.Same(snapshot, WorkerHistorySnapshotTestHelpers.GetSnapshots(session.Server)["instance"]);
        await CompleteAsync(session.Server, delivery);
        await CompleteAsync(session.Server, other);
        await Task.WhenAll(execution, otherExecution).WaitAsync(Timeout);
    }

    /// <summary>
    /// A rejected chunk and a valid final response compete for one delivery's settlement.
    /// </summary>
    [Fact]
    public async Task CompleteOrchestrator_ChunkAndFullResponseOnlyOneClaimsDeliveryAsync()
    {
        // Arrange
        await using ServerSession session = new();
        Task execution = session.StartExecution(activity: false, historyPayload: 'x');
        P.WorkItem delivery = await session.ReadAsync();
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
#pragma warning disable CS0612 // Exercise legacy chunk rejection racing a whole response.
        P.OrchestratorResponse fragment = new()
        {
            InstanceId = delivery.OrchestratorRequest.InstanceId,
            CompletionToken = delivery.CompletionToken,
            IsPartial = true,
        };
#pragma warning restore CS0612
        Task<StatusCode> chunk = Task.Run(() => CompleteAfterSignalAsync(
            start.Task, () => session.Server.CompleteOrchestratorTask(fragment, CreateContext())));
        Task<StatusCode> full = Task.Run(() => CompleteAfterSignalAsync(
            start.Task, () => CompleteAsync(session.Server, delivery, "\"accepted\"")));

        // Act
        start.SetResult();
        StatusCode[] outcomes = await Task.WhenAll(chunk, full).WaitAsync(Timeout);
        GrpcOrchestratorExecutionResult result = await ((Task<GrpcOrchestratorExecutionResult>)execution).WaitAsync(Timeout);

        // Assert
        if (outcomes[0] == StatusCode.InvalidArgument)
        {
            Assert.Equal(StatusCode.NotFound, outcomes[1]);
            Assert.Equal(OrchestrationStatus.Failed,
                Assert.IsType<OrchestrationCompleteOrchestratorAction>(Assert.Single(result.Actions)).OrchestrationStatus);
        }
        else
        {
            Assert.Equal(StatusCode.NotFound, outcomes[0]);
            Assert.Equal(StatusCode.OK, outcomes[1]);
            Assert.Empty(result.Actions);
            Assert.Equal("\"accepted\"", result.CustomStatus);
        }

        Assert.Empty(WorkerHistorySnapshotTestHelpers.GetSnapshots(session.Server));
    }

    /// <summary>
    /// Failed-send cleanup cannot consume a replacement delivery or remove its replay snapshot.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispatch_FailedSendDoesNotRemoveReplacementAsync(bool activity)
    {
        // Arrange
        TaskCompletionSource releaseWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int writes = 0;
        await using ServerSession session = new(async _ =>
        {
            if (Interlocked.Increment(ref writes) == 1)
            {
                await releaseWrite.Task.WaitAsync(Timeout);
                throw new InvalidOperationException("Expected send failure");
            }
        });
        Task firstExecution = session.StartExecution(activity, historyPayload: 'x');
        P.WorkItem first = await session.ReadAsync();

        try
        {
            Task nextExecution = session.StartExecution(activity, historyPayload: 'y');

            // Act
            releaseWrite.SetResult();
            await Assert.ThrowsAsync<InvalidOperationException>(() => firstExecution.WaitAsync(Timeout));
            P.WorkItem next = await session.ReadAsync();

            // Assert
            Assert.NotEqual(first.CompletionToken, next.CompletionToken);
            RpcException lateCompletion = await Assert.ThrowsAsync<RpcException>(() => CompleteAsync(session.Server, first));
            Assert.Equal(StatusCode.NotFound, lateCompletion.StatusCode);
            Assert.False(nextExecution.IsCompleted);
            if (!activity)
            {
                Assert.Equal('y', WorkerHistorySnapshotTestHelpers.GetSnapshots(session.Server)["instance"][0].ExecutionStarted.Input![0]);
            }

            await CompleteAsync(session.Server, next);
            await nextExecution.WaitAsync(Timeout);
            if (!activity)
            {
                GrpcOrchestratorExecutionResult result = await (Task<GrpcOrchestratorExecutionResult>)nextExecution;
                Assert.Empty(result.Actions);
                Assert.Equal("\"result\"", result.CustomStatus);
                Assert.Empty(WorkerHistorySnapshotTestHelpers.GetSnapshots(session.Server));
            }
        }
        finally
        {
            releaseWrite.TrySetResult();
        }
    }

    /// <summary>
    /// A send failure cannot override a completion accepted while that send was still pending.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Dispatch_CompletedDeliveryPreservesResultAfterSendFailureAsync(bool activity, bool chunked)
    {
        // Arrange
        TaskCompletionSource releaseWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using ServerSession session = new(async _ =>
        {
            await releaseWrite.Task.WaitAsync(Timeout);
            throw new InvalidOperationException("Expected send failure");
        });
        Task execution = session.StartExecution(activity, historyPayload: 'x');
        P.WorkItem delivery = await session.ReadAsync();

        try
        {
            // Act
            if (activity)
            {
                await session.Server.CompleteActivityTask(new()
                {
                    InstanceId = delivery.ActivityRequest.OrchestrationInstance.InstanceId,
                    TaskId = delivery.ActivityRequest.TaskId,
                    CompletionToken = delivery.CompletionToken,
                    Result = "\"accepted result\"",
                }, CreateContext());
            }
            else
            {
                P.OrchestratorResponse response = new()
                {
                    InstanceId = delivery.OrchestratorRequest.InstanceId,
                    CompletionToken = delivery.CompletionToken,
                    CustomStatus = "\"accepted status\"",
                    Actions = { new P.OrchestratorAction
                    {
                        Id = 7,
                        ScheduleTask = new() { Name = "Accepted", Input = "\"accepted input\"" },
                    } },
                };
                if (chunked)
                {
#pragma warning disable CS0612 // A terminal unsupported-chunk result also owns settlement over a late send failure.
                    response.IsPartial = true;
#pragma warning restore CS0612
                    RpcException unsupported = await Assert.ThrowsAsync<RpcException>(() =>
                        session.Server.CompleteOrchestratorTask(response, CreateContext()));
                    Assert.Equal(StatusCode.InvalidArgument, unsupported.StatusCode);
                }
                else
                {
                    await session.Server.CompleteOrchestratorTask(response, CreateContext());
                }
            }

            Assert.False(execution.IsCompleted);
            releaseWrite.SetResult();

            // Assert
            if (activity)
            {
                ActivityExecutionResult result = await ((Task<ActivityExecutionResult>)execution).WaitAsync(Timeout);
                TaskCompletedEvent completed = Assert.IsType<TaskCompletedEvent>(result.ResponseEvent);
                Assert.Equal(delivery.ActivityRequest.TaskId, completed.TaskScheduledId);
                Assert.Equal("\"accepted result\"", completed.Result);
            }
            else
            {
                GrpcOrchestratorExecutionResult result = await ((Task<GrpcOrchestratorExecutionResult>)execution).WaitAsync(Timeout);
                if (chunked)
                {
                    OrchestrationCompleteOrchestratorAction failure = Assert.IsType<OrchestrationCompleteOrchestratorAction>(Assert.Single(result.Actions));
                    Assert.Equal(OrchestrationStatus.Failed, failure.OrchestrationStatus);
                    Assert.Equal(typeof(NotSupportedException).FullName, failure.FailureDetails?.ErrorType);
                }
                else
                {
                    ScheduleTaskOrchestratorAction action = Assert.IsAssignableFrom<ScheduleTaskOrchestratorAction>(Assert.Single(result.Actions));
                    Assert.Equal(7, action.Id);
                    Assert.Equal("Accepted", action.Name);
                    Assert.Equal("\"accepted input\"", action.Input);
                    Assert.Equal("\"accepted status\"", result.CustomStatus);
                }

                Assert.Empty(WorkerHistorySnapshotTestHelpers.GetSnapshots(session.Server));
            }
        }
        finally
        {
            releaseWrite.TrySetResult();
        }
    }

    /// <summary>
    /// Disconnecting a work-item stream does not implicitly settle delivered work.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disconnect_DoesNotSettleDeliveredWorkAsync(bool activity)
    {
        // Arrange
        await using ServerSession session = new();
        Task execution = session.StartExecution(activity);
        P.WorkItem delivery = await session.ReadAsync();

        // Act
        await session.DisconnectAsync();

        // Assert
        Assert.False(execution.IsCompleted);
        await CompleteAsync(session.Server, delivery);
        await execution.WaitAsync(Timeout);
    }

    static Task CompleteAsync(TaskHubGrpcServer server, P.WorkItem delivery, string value = "\"result\"") =>
        delivery.ActivityRequest is { } activity
            ? server.CompleteActivityTask(new()
            {
                InstanceId = activity.OrchestrationInstance.InstanceId,
                TaskId = activity.TaskId,
                CompletionToken = delivery.CompletionToken,
                Result = value,
            }, CreateContext())
            : server.CompleteOrchestratorTask(new()
            {
                InstanceId = delivery.OrchestratorRequest.InstanceId,
                CompletionToken = delivery.CompletionToken,
                CustomStatus = value,
            }, CreateContext());

    static async Task<StatusCode> CompleteAfterSignalAsync(Task signal, Func<Task> complete)
    {
        await signal;
        try
        {
            await complete();
            return StatusCode.OK;
        }
        catch (RpcException exception)
        {
            return exception.StatusCode;
        }
    }

    static ServerCallContext CreateContext(CancellationToken cancellation = default)
    {
        Mock<ServerCallContext> context = new();
        context.Protected().SetupGet<CancellationToken>("CancellationTokenCore").Returns(cancellation);
        return context.Object;
    }

    sealed class ServerSession : IAsyncDisposable
    {
        readonly CancellationTokenSource stopping = new();
        readonly CancellationTokenSource connectionCancellation = new();
        readonly Channel<P.WorkItem> workItems = Channel.CreateUnbounded<P.WorkItem>();
        readonly List<P.WorkItem> deliveries = new();
        readonly List<Task> executions = new();
        readonly Task connection;

        internal ServerSession(Func<P.WorkItem, Task>? afterWrite = null)
        {
            InMemoryOrchestrationService service = new();
            this.Server = new(
                Mock.Of<IHostApplicationLifetime>(lifetime => lifetime.ApplicationStopping == this.stopping.Token),
                NullLoggerFactory.Instance, service, service, Options.Create(new TaskHubGrpcServerOptions()));
            Mock<IServerStreamWriter<P.WorkItem>> writer = new();
            writer.Setup(w => w.WriteAsync(It.IsAny<P.WorkItem>())).Returns<P.WorkItem>(async item =>
            {
                this.deliveries.Add(item);
                await this.workItems.Writer.WriteAsync(item);
                if (afterWrite is not null)
                {
                    await afterWrite(item);
                }
            });
            this.connection = this.Server.GetWorkItems(
                new() { Capabilities = { P.WorkerCapability.HistoryStreaming } },
                writer.Object, CreateContext(this.connectionCancellation.Token));
        }

        internal TaskHubGrpcServer Server { get; }

        internal Task StartExecution(bool activity, string instanceId = "instance", char? historyPayload = null)
        {
            OrchestrationInstance instance = new() { InstanceId = instanceId, ExecutionId = "current" };
            HistoryEvent[] history = historyPayload is { } payload
                ? [
                    new ExecutionStartedEvent(-1, new string(payload, 600 * 1024)) { Name = "Orchestrator", Version = string.Empty, OrchestrationInstance = instance },
                    new TaskScheduledEvent(0, "Activity", string.Empty, new string(payload, 600 * 1024)),
                ]
                : [];
            ITaskExecutor executor = this.Server;
            Task execution = activity
                ? executor.ExecuteActivity(instance, new TaskScheduledEvent(1, "Activity", string.Empty, null))
                : executor.ExecuteOrchestrator(instance, history, [
                    new ExecutionStartedEvent(-1, null) { Name = "Orchestrator", Version = string.Empty, OrchestrationInstance = instance },
                ]);
            this.executions.Add(execution);
            return execution;
        }

        internal Task<P.WorkItem> ReadAsync() => this.workItems.Reader.ReadAsync().AsTask().WaitAsync(Timeout);

        internal async Task DisconnectAsync()
        {
            this.connectionCancellation.Cancel();
            await this.connection.WaitAsync(Timeout);
        }

        public async ValueTask DisposeAsync()
        {
            using CancellationTokenSource stopping = this.stopping;
            using CancellationTokenSource connectionCancellation = this.connectionCancellation;
            try
            {
                foreach (P.WorkItem delivery in this.deliveries)
                {
                    try
                    {
                        await CompleteAsync(this.Server, delivery);
                    }
                    catch (RpcException exception) when (exception.StatusCode == StatusCode.NotFound)
                    {
                        // Already completed or removed after a send failure.
                    }
                }

                foreach (Task execution in this.executions.Where(task => !task.IsCompleted))
                {
                    await execution.WaitAsync(Timeout);
                }
            }
            finally
            {
                stopping.Cancel();
                await this.DisconnectAsync();
                this.Server.Dispose();
            }
        }
    }
}
