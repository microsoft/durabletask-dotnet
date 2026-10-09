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
/// Tests explicit abandonment and the ownership of individual work-item deliveries.
/// </summary>
public class WorkItemAbandonmentTests
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Explicit abandonment settles the execution awaited by the dispatcher.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AbandonWorkItem_CancelsPendingExecutionAsync(bool activity)
    {
        // Arrange
        await using ServerSession session = new();
        Task execution = session.StartExecution(activity);
        P.WorkItem delivery = await session.ReadAsync();

        // Act
        await AbandonAsync(session.Server, delivery);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution.WaitAsync(Timeout));
        Assert.True(execution.IsCanceled);
    }

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
        RpcException lateAbandon = await Assert.ThrowsAsync<RpcException>(() => AbandonAsync(session.Server, first));
        Assert.Equal(StatusCode.NotFound, lateAbandon.StatusCode);
        Assert.False(nextExecution.IsCompleted);
        await CompleteAsync(session.Server, next);
        await nextExecution.WaitAsync(Timeout);
    }

    /// <summary>
    /// Stale abandonment and completion cannot affect a replacement or an unrelated delivery.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AbandonWorkItem_StaleTokenDoesNotSettleRedeliveryAsync(bool activity)
    {
        // Arrange
        await using ServerSession session = new();
        Task firstExecution = session.StartExecution(activity);
        P.WorkItem first = await session.ReadAsync();
        await AbandonAsync(session.Server, first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstExecution.WaitAsync(Timeout));
        Task replacementExecution = session.StartExecution(activity);
        P.WorkItem replacement = await session.ReadAsync();
        Task otherExecution = session.StartExecution(activity, "other");
        P.WorkItem other = await session.ReadAsync();

        // Act
        RpcException duplicate = await Assert.ThrowsAsync<RpcException>(() => AbandonAsync(session.Server, first));
        RpcException lateCompletion = await Assert.ThrowsAsync<RpcException>(() => CompleteAsync(session.Server, first));

        // Assert
        Assert.Equal(StatusCode.NotFound, duplicate.StatusCode);
        Assert.Equal(StatusCode.NotFound, lateCompletion.StatusCode);
        Assert.NotEqual(first.CompletionToken, replacement.CompletionToken);
        Assert.False(replacementExecution.IsCompleted);
        Assert.False(otherExecution.IsCompleted);
        await CompleteAsync(session.Server, replacement);
        await CompleteAsync(session.Server, other);
        await Task.WhenAll(replacementExecution, otherExecution).WaitAsync(Timeout);
    }

    /// <summary>
    /// Missing, unknown, and wrong-kind abandonment tokens leave active deliveries untouched.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AbandonWorkItem_InvalidTokensAreRejectedAsync(bool activity)
    {
        // Arrange
        await using ServerSession session = new();
        Task execution = session.StartExecution(activity);
        P.WorkItem delivery = await session.ReadAsync();
        Task otherExecution = session.StartExecution(!activity, "other");
        P.WorkItem other = await session.ReadAsync();

        // Act
        RpcException empty = await Assert.ThrowsAsync<RpcException>(() => AbandonTokenAsync(session.Server, activity, string.Empty));
        RpcException unknown = await Assert.ThrowsAsync<RpcException>(() => AbandonTokenAsync(session.Server, activity, "unknown"));
        RpcException wrongKind = await Assert.ThrowsAsync<RpcException>(() => AbandonTokenAsync(session.Server, activity, other.CompletionToken));

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
    /// Completion checks both the delivery token and the logical work-item identity.
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
        P.WorkItem missingToken = delivery.Clone();
        missingToken.CompletionToken = string.Empty;
        P.WorkItem wrongInstance = delivery.Clone();
        if (activity)
        {
            wrongInstance.ActivityRequest.OrchestrationInstance.InstanceId = "other";
        }
        else
        {
            wrongInstance.OrchestratorRequest.InstanceId = "other";
        }

        // Act
        RpcException missing = await Assert.ThrowsAsync<RpcException>(() => CompleteAsync(session.Server, missingToken));
        RpcException mismatch = await Assert.ThrowsAsync<RpcException>(() => CompleteAsync(session.Server, wrongInstance));

        // Assert
        Assert.Equal(StatusCode.InvalidArgument, missing.StatusCode);
        Assert.Equal(StatusCode.InvalidArgument, mismatch.StatusCode);
        Assert.False(execution.IsCompleted);
        if (activity)
        {
            P.WorkItem wrongTask = delivery.Clone();
            wrongTask.ActivityRequest.TaskId++;
            RpcException wrongTaskId = await Assert.ThrowsAsync<RpcException>(() => CompleteAsync(session.Server, wrongTask));
            Assert.Equal(StatusCode.InvalidArgument, wrongTaskId.StatusCode);
            Assert.False(execution.IsCompleted);
        }

        await CompleteAsync(session.Server, delivery);
        await execution.WaitAsync(Timeout);
    }

    /// <summary>
    /// Concurrent completion and abandonment have exactly one winner.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteAndAbandon_OnlyOneClaimSucceedsAsync(bool activity)
    {
        // Arrange
        await using ServerSession session = new();
        Task execution = session.StartExecution(activity);
        P.WorkItem delivery = await session.ReadAsync();
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<StatusCode> completion = Task.Run(() => SettleAfterSignalAsync(
            start.Task, () => CompleteAsync(session.Server, delivery)));
        Task<StatusCode> abandonment = Task.Run(() => SettleAfterSignalAsync(
            start.Task, () => AbandonAsync(session.Server, delivery)));

        // Act
        start.SetResult();
        StatusCode[] outcomes = await Task.WhenAll(completion, abandonment).WaitAsync(Timeout);

        // Assert
        Assert.Single(outcomes, status => status == StatusCode.OK);
        Assert.Single(outcomes, status => status == StatusCode.NotFound);
        if (outcomes[0] == StatusCode.OK)
        {
            await execution.WaitAsync(Timeout);
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution.WaitAsync(Timeout));
        }
    }

    /// <summary>
    /// Activity responses still carry successful results and application failures.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteActivityTask_PreservesResultAsync(bool failed)
    {
        // Arrange
        await using ServerSession session = new();
        Task<ActivityExecutionResult> execution = session.Executor.ExecuteActivity(
            new() { InstanceId = "instance", ExecutionId = "current" }, new TaskScheduledEvent(1, "Activity", string.Empty, null));
        P.WorkItem delivery = await session.ReadAsync();

        // Act
        await session.Server.CompleteActivityTask(new()
        {
            InstanceId = "instance",
            TaskId = 1,
            CompletionToken = delivery.CompletionToken,
            Result = "\"result\"",
            FailureDetails = failed ? new() { ErrorType = "ExpectedFailure", ErrorMessage = "failed" } : null,
        }, CreateContext());
        ActivityExecutionResult result = await execution.WaitAsync(Timeout);

        // Assert
        if (failed)
        {
            TaskFailedEvent failure = Assert.IsType<TaskFailedEvent>(result.ResponseEvent);
            Assert.Equal(1, failure.TaskScheduledId);
            Assert.Equal("ExpectedFailure", failure.FailureDetails?.ErrorType);
        }
        else
        {
            TaskCompletedEvent completed = Assert.IsType<TaskCompletedEvent>(result.ResponseEvent);
            Assert.Equal(1, completed.TaskScheduledId);
            Assert.Equal("\"result\"", completed.Result);
        }

        RpcException lateAbandon = await Assert.ThrowsAsync<RpcException>(() => AbandonAsync(session.Server, delivery));
        Assert.Equal(StatusCode.NotFound, lateAbandon.StatusCode);
    }

    /// <summary>
    /// Abandonment discards partial actions and the snapshot without contaminating the next episode.
    /// </summary>
    [Fact]
    public async Task AbandonOrchestrator_ReleasesPartialResponseAndSnapshotAsync()
    {
        // Arrange
        await using ServerSession session = new();
        Task firstExecution = session.StartExecution(activity: false, historyPayload: 'x');
        P.WorkItem first = await session.ReadAsync();
        Assert.True(first.OrchestratorRequest.RequiresHistoryStreaming);
        Assert.NotEmpty(await session.ReadHistoryAsync());
        await AddPartialResponseAsync(session.Server, first, "Discarded");
        Assert.False(firstExecution.IsCompleted);

        // Act
        await AbandonAsync(session.Server, first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstExecution.WaitAsync(Timeout));

        // Assert
        Assert.Empty(WorkerHistorySnapshotTestHelpers.GetSnapshots(session.Server));
        Assert.Empty(await session.ReadHistoryAsync());
        Task nextExecution = session.StartExecution(activity: false, historyPayload: 'y');
        P.WorkItem next = await session.ReadAsync();
        List<P.HistoryEvent> nextHistory = await session.ReadHistoryAsync();
        RpcException latePartial = await Assert.ThrowsAsync<RpcException>(() => AddPartialResponseAsync(session.Server, first, "Late"));
        Assert.Equal(StatusCode.NotFound, latePartial.StatusCode);
        Assert.Equal(nextHistory, await session.ReadHistoryAsync());
        await AddPartialResponseAsync(session.Server, next, "Current");
        await session.Server.CompleteOrchestratorTask(new()
        {
            InstanceId = next.OrchestratorRequest.InstanceId,
            CompletionToken = next.CompletionToken,
            CustomStatus = "\"current\"",
        }, CreateContext());
        GrpcOrchestratorExecutionResult result = await ((Task<GrpcOrchestratorExecutionResult>)nextExecution).WaitAsync(Timeout);
        Assert.Equal("Current", Assert.IsAssignableFrom<ScheduleTaskOrchestratorAction>(Assert.Single(result.Actions)).Name);
        Assert.Equal("\"current\"", result.CustomStatus);
        Assert.Empty(WorkerHistorySnapshotTestHelpers.GetSnapshots(session.Server));
    }

    /// <summary>
    /// An already captured history reader survives abandonment without reading the replacement's snapshot.
    /// </summary>
    [Fact]
    public async Task AbandonOrchestrator_PreservesCapturedReaderAndReplacementSnapshotAsync()
    {
        // Arrange
        await using ServerSession session = new();
        Task firstExecution = session.StartExecution(activity: false, historyPayload: 'x');
        P.WorkItem first = await session.ReadAsync();
        List<P.HistoryEvent> firstHistory = await session.ReadHistoryAsync();
        TaskCompletionSource firstChunk = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseReader = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<P.HistoryChunk> chunks = new();
        Mock<IServerStreamWriter<P.HistoryChunk>> writer = new();
        writer.Setup(stream => stream.WriteAsync(It.IsAny<P.HistoryChunk>())).Returns<P.HistoryChunk>(async chunk =>
        {
            chunks.Add(chunk.Clone());
            if (chunks.Count == 1)
            {
                firstChunk.SetResult();
                await releaseReader.Task.WaitAsync(Timeout);
            }
        });
        Task reader = session.Server.StreamInstanceHistory(
            new() { InstanceId = "instance", ForWorkItemProcessing = true }, writer.Object, CreateContext());

        try
        {
            await firstChunk.Task.WaitAsync(Timeout);

            // Act
            await AbandonAsync(session.Server, first);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstExecution.WaitAsync(Timeout));
            Task nextExecution = session.StartExecution(activity: false, historyPayload: 'y');
            P.WorkItem next = await session.ReadAsync();
            List<P.HistoryEvent> nextHistory = await session.ReadHistoryAsync();
            releaseReader.SetResult();
            await reader.WaitAsync(Timeout);

            // Assert
            Assert.Equal(2, chunks.Count);
            Assert.Equal(firstHistory, chunks.SelectMany(chunk => chunk.Events));
            Assert.Equal(nextHistory, await session.ReadHistoryAsync());
            Assert.False(nextExecution.IsCompleted);
            await CompleteAsync(session.Server, next);
            await nextExecution.WaitAsync(Timeout);
            Assert.Empty(WorkerHistorySnapshotTestHelpers.GetSnapshots(session.Server));
        }
        finally
        {
            releaseReader.TrySetResult();
            await reader.WaitAsync(Timeout);
        }
    }

    /// <summary>
    /// Send-failure cleanup from an abandoned delivery cannot erase the replacement's ownership.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispatch_FailedOldSendDoesNotRemoveReplacementAsync(bool activity)
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
            await AbandonAsync(session.Server, first);
            Task nextExecution = session.StartExecution(activity, historyPayload: 'y');

            // Act
            releaseWrite.TrySetResult();
            await Assert.ThrowsAsync<InvalidOperationException>(() => firstExecution.WaitAsync(Timeout));
            P.WorkItem next = await session.ReadAsync();

            // Assert
            Assert.NotEqual(first.CompletionToken, next.CompletionToken);
            Assert.False(nextExecution.IsCompleted);
            if (!activity)
            {
                Assert.NotEmpty(await session.ReadHistoryAsync());
                Assert.Equal('y', WorkerHistorySnapshotTestHelpers.GetSnapshots(session.Server)["instance"][0].ExecutionStarted.Input![0]);
            }

            await CompleteAsync(session.Server, next);
            await nextExecution.WaitAsync(Timeout);
        }
        finally
        {
            releaseWrite.TrySetResult();
        }
    }

    /// <summary>
    /// A disconnected work-item stream does not implicitly abandon already delivered work.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disconnect_DoesNotAbandonDeliveredWorkAsync(bool activity)
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

    static Task AbandonAsync(TaskHubGrpcServer server, P.WorkItem delivery) =>
        AbandonTokenAsync(server, delivery.ActivityRequest is not null, delivery.CompletionToken);

    static Task AbandonTokenAsync(TaskHubGrpcServer server, bool activity, string completionToken) =>
        activity
            ? server.AbandonTaskActivityWorkItem(new() { CompletionToken = completionToken }, CreateContext())
            : server.AbandonTaskOrchestratorWorkItem(new() { CompletionToken = completionToken }, CreateContext());

    static Task CompleteAsync(TaskHubGrpcServer server, P.WorkItem delivery) =>
        delivery.ActivityRequest is { } activity
            ? server.CompleteActivityTask(new()
            {
                InstanceId = activity.OrchestrationInstance.InstanceId,
                TaskId = activity.TaskId,
                CompletionToken = delivery.CompletionToken,
                Result = "\"result\"",
            }, CreateContext())
            : server.CompleteOrchestratorTask(new()
            {
                InstanceId = delivery.OrchestratorRequest.InstanceId,
                CompletionToken = delivery.CompletionToken,
            }, CreateContext());

    static Task AddPartialResponseAsync(TaskHubGrpcServer server, P.WorkItem delivery, string activityName)
    {
#pragma warning disable CS0612 // Exercise legacy chunked responses with per-delivery ownership.
        return server.CompleteOrchestratorTask(new()
        {
            InstanceId = delivery.OrchestratorRequest.InstanceId,
            CompletionToken = delivery.CompletionToken,
            IsPartial = true,
            Actions = { new P.OrchestratorAction { Id = 0, ScheduleTask = new() { Name = activityName } } },
        }, CreateContext());
#pragma warning restore CS0612
    }

    static async Task<StatusCode> SettleAfterSignalAsync(Task signal, Func<Task> settle)
    {
        await signal;
        try
        {
            await settle();
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

        internal ITaskExecutor Executor => this.Server;

        internal Task StartExecution(bool activity, string instanceId = "instance", char? historyPayload = null)
        {
            OrchestrationInstance instance = new() { InstanceId = instanceId, ExecutionId = "current" };
            HistoryEvent[] history = historyPayload is { } payload
                ? [
                    new ExecutionStartedEvent(-1, new string(payload, 600 * 1024)) { Name = "Orchestrator", Version = string.Empty, OrchestrationInstance = instance },
                    new TaskScheduledEvent(0, "Activity", string.Empty, new string(payload, 600 * 1024)),
                ]
                : [];
            Task execution = activity
                ? this.Executor.ExecuteActivity(instance, new TaskScheduledEvent(1, "Activity", string.Empty, null))
                : this.Executor.ExecuteOrchestrator(instance, history, [
                    new ExecutionStartedEvent(-1, null) { Name = "Orchestrator", Version = string.Empty, OrchestrationInstance = instance },
                ]);
            this.executions.Add(execution);
            return execution;
        }

        internal Task<P.WorkItem> ReadAsync() => this.workItems.Reader.ReadAsync().AsTask().WaitAsync(Timeout);

        internal async Task<List<P.HistoryEvent>> ReadHistoryAsync()
        {
            List<P.HistoryEvent> history = new();
            Mock<IServerStreamWriter<P.HistoryChunk>> writer = new();
            writer.Setup(w => w.WriteAsync(It.IsAny<P.HistoryChunk>())).Returns<P.HistoryChunk>(chunk =>
            {
                history.AddRange(chunk.Events);
                return Task.CompletedTask;
            });
            await this.Server.StreamInstanceHistory(
                new() { InstanceId = "instance", ForWorkItemProcessing = true }, writer.Object, CreateContext());
            return history;
        }

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
                        // Already completed or abandoned by the test.
                    }
                }

                foreach (Task execution in this.executions.Where(task => !task.IsCompleted))
                {
                    try
                    {
                        await execution.WaitAsync(Timeout);
                    }
                    catch (OperationCanceledException)
                    {
                        // Explicitly abandoned executions are expected to be canceled.
                    }
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
