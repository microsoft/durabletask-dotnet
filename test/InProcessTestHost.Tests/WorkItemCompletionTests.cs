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
        Task<StatusCode> first = Task.Run(() => CompleteAfterSignalAsync(start.Task, session.Server, delivery, "\"first\""));
        Task<StatusCode> second = Task.Run(() => CompleteAfterSignalAsync(start.Task, session.Server, delivery, "\"second\""));

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
    /// Unsupported response fragments leave the delivery and its replay snapshot available for a full response.
    /// </summary>
    [Theory]
    [InlineData(true, null)]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    public async Task CompleteOrchestrator_RejectsChunkedResponsesWithoutSettlingDeliveryAsync(bool partial, int? chunkIndex)
    {
        // Arrange
        await using ServerSession session = new();
        Task execution = session.StartExecution(activity: false, historyPayload: 'x');
        P.WorkItem delivery = await session.ReadAsync();
        List<P.HistoryEvent> snapshot = WorkerHistorySnapshotTestHelpers.GetSnapshots(session.Server)["instance"];
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
        Assert.False(execution.IsCompleted);
        Assert.Same(snapshot, WorkerHistorySnapshotTestHelpers.GetSnapshots(session.Server)["instance"]);
        await session.Server.CompleteOrchestratorTask(new()
        {
            InstanceId = delivery.OrchestratorRequest.InstanceId,
            CompletionToken = delivery.CompletionToken,
            CustomStatus = "\"accepted status\"",
            Actions = { new P.OrchestratorAction
            {
                Id = 7,
                ScheduleTask = new() { Name = "Accepted", Input = "\"accepted input\"" },
            } },
        }, CreateContext());
        GrpcOrchestratorExecutionResult result = await ((Task<GrpcOrchestratorExecutionResult>)execution).WaitAsync(Timeout);
        ScheduleTaskOrchestratorAction action = Assert.IsAssignableFrom<ScheduleTaskOrchestratorAction>(Assert.Single(result.Actions));
        Assert.Equal(7, action.Id);
        Assert.Equal("Accepted", action.Name);
        Assert.Equal("\"accepted input\"", action.Input);
        Assert.Equal("\"accepted status\"", result.CustomStatus);
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispatch_CompletedDeliveryPreservesResultAfterSendFailureAsync(bool activity)
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
                await session.Server.CompleteOrchestratorTask(new()
                {
                    InstanceId = delivery.OrchestratorRequest.InstanceId,
                    CompletionToken = delivery.CompletionToken,
                    CustomStatus = "\"accepted status\"",
                    Actions = { new P.OrchestratorAction
                    {
                        Id = 7,
                        ScheduleTask = new() { Name = "Accepted", Input = "\"accepted input\"" },
                    } },
                }, CreateContext());
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
                ScheduleTaskOrchestratorAction action = Assert.IsAssignableFrom<ScheduleTaskOrchestratorAction>(Assert.Single(result.Actions));
                Assert.Equal(7, action.Id);
                Assert.Equal("Accepted", action.Name);
                Assert.Equal("\"accepted input\"", action.Input);
                Assert.Equal("\"accepted status\"", result.CustomStatus);
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

    /// <summary>
    /// Abandon RPCs still only acknowledge requests without validating tokens or settling work.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AbandonWorkItem_RemainsAcknowledgementOnlyAsync(bool activity)
    {
        // Arrange
        await using ServerSession session = new();
        Task execution = session.StartExecution(activity);
        P.WorkItem delivery = await session.ReadAsync();

        // Act
        foreach (string token in new[] { delivery.CompletionToken, string.Empty, "unknown", delivery.CompletionToken })
        {
            if (activity)
            {
                await session.Server.AbandonTaskActivityWorkItem(new() { CompletionToken = token }, CreateContext());
            }
            else
            {
                await session.Server.AbandonTaskOrchestratorWorkItem(new() { CompletionToken = token }, CreateContext());
            }
        }

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

    static async Task<StatusCode> CompleteAfterSignalAsync(Task signal, TaskHubGrpcServer server, P.WorkItem delivery, string value)
    {
        await signal;
        try
        {
            await CompleteAsync(server, delivery, value);
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
                this.stopping.Cancel();
                await this.DisconnectAsync();
                this.Server.Dispose();
                this.connectionCancellation.Dispose();
                this.stopping.Dispose();
            }
        }
    }
}
