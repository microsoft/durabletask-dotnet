// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.History;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Sidecar.Dispatcher;

/// <summary>
/// Dispatches and manages the execution of <see cref="TaskActivityWorkItem"/> instances.
/// </summary>
class TaskActivityDispatcher : WorkItemDispatcher<TaskActivityWorkItem>
{
    readonly IOrchestrationService service;
    readonly ITaskExecutor taskExecutor;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskActivityDispatcher"/> class.
    /// </summary>
    /// <param name="log">The logger used for diagnostic output.</param>
    /// <param name="trafficSignal">A signal used to control dispatcher activity.</param>
    /// <param name="service">The orchestration service that manages task activity work items.</param>
    /// <param name="taskExecutor">The task executor responsible for running activity code.</param>
    public TaskActivityDispatcher(ILogger log, ITrafficSignal trafficSignal, IOrchestrationService service, ITaskExecutor taskExecutor)
        : base(log, trafficSignal)
    {
        this.service = service;
        this.taskExecutor = taskExecutor;
    }

    /// <summary>
    /// Gets the maximum number of concurrent work items allowed by the underlying orchestration service.
    /// </summary>
    public override int MaxWorkItems => this.service.MaxConcurrentTaskActivityWorkItems;

    /// <summary>
    /// Abandons a task activity work item, releasing any associated locks.
    /// </summary>
    /// <param name="workItem">The work item to abandon.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public override Task AbandonWorkItemAsync(TaskActivityWorkItem workItem) =>
        this.service.AbandonTaskActivityWorkItemAsync(workItem);

    /// <summary>
    /// Attempts to fetch the next available <see cref="TaskActivityWorkItem"/> from the orchestration service.
    /// </summary>
    /// <param name="timeout">The maximum duration to wait for an available work item.</param>
    /// <param name="cancellationToken">A token to signal operation cancellation.</param>
    /// <returns>
    /// A task that returns the locked work item, or <c>null</c> if none is available.
    /// </returns>
    public override Task<TaskActivityWorkItem?> FetchWorkItemAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        this.service.LockNextTaskActivityWorkItem(timeout, cancellationToken);

    /// <summary>
    /// Determines the delay, in seconds, to wait before retrying after a fetch exception occurs.
    /// </summary>
    /// <param name="ex">The exception that occurred during fetch.</param>
    /// <returns>The delay duration in seconds before the next fetch attempt.</returns>
    public override int GetDelayInSecondsOnFetchException(Exception ex) =>
        this.service.GetDelayInSecondsAfterOnFetchException(ex);

    /// <summary>
    /// Retrieves a unique identifier for the specified work item.
    /// </summary>
    /// <param name="workItem">The work item for which to retrieve the ID.</param>
    /// <returns>The unique identifier for the work item.</returns>
    public override string GetWorkItemId(TaskActivityWorkItem workItem) => workItem.Id;

    /// <summary>
    /// Not Implemented.
    /// </summary>
    /// <param name="workItem">The work item to release.</param>
    /// <returns>A completed task.</returns>
    public override Task ReleaseWorkItemAsync(TaskActivityWorkItem workItem) => Task.CompletedTask;

    /// <summary>
    /// Renews the lock on the specified task activity work item to prevent expiration.
    /// </summary>
    /// <param name="workItem">The work item to renew.</param>
    /// <returns>
    /// A task that returns the renewed work item upon successful lock renewal.
    /// </returns>
    public override Task<TaskActivityWorkItem> RenewWorkItemAsync(TaskActivityWorkItem workItem) =>
        this.service.RenewTaskActivityWorkItemLockAsync(workItem);

    /// <summary>
    /// Executes the specified <see cref="TaskActivityWorkItem"/> using the configured <see cref="ITaskExecutor"/>.
    /// </summary>
    /// <param name="workItem">The work item to execute.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    protected override async Task ExecuteWorkItemAsync(TaskActivityWorkItem workItem)
    {
        TaskScheduledEvent scheduledEvent = (TaskScheduledEvent)workItem.TaskMessage.Event;

        // TODO: Error handling for internal errors (user code exceptions are handled by the executor).
        ActivityExecutionResult result = await this.taskExecutor.ExecuteActivity(
            instance: workItem.TaskMessage.OrchestrationInstance,
            activityEvent: scheduledEvent);

        TaskMessage responseMessage = new()
        {
            Event = result.ResponseEvent,
            OrchestrationInstance = workItem.TaskMessage.OrchestrationInstance,
        };

        await this.service.CompleteTaskActivityWorkItemAsync(workItem, responseMessage);
    }
}
