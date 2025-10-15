// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.History;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Sidecar.Dispatcher;

class TaskActivityDispatcher : WorkItemDispatcher<TaskActivityWorkItem>
{
    readonly IOrchestrationService service;
    readonly ITaskExecutor taskExecutor;

    public TaskActivityDispatcher(ILogger log, ITrafficSignal trafficSignal, IOrchestrationService service, ITaskExecutor taskExecutor)
        : base(log, trafficSignal)
    {
        this.service = service;
        this.taskExecutor = taskExecutor;
    }

    public override int MaxWorkItems => this.service.MaxConcurrentTaskActivityWorkItems;

    public override Task AbandonWorkItemAsync(TaskActivityWorkItem workItem) =>
        this.service.AbandonTaskActivityWorkItemAsync(workItem);

    public override Task<TaskActivityWorkItem?> FetchWorkItemAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        this.service.LockNextTaskActivityWorkItem(timeout, cancellationToken);

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

    public override int GetDelayInSecondsOnFetchException(Exception ex) =>
        this.service.GetDelayInSecondsAfterOnFetchException(ex);

    public override string GetWorkItemId(TaskActivityWorkItem workItem) => workItem.Id;

    // No-op
    public override Task ReleaseWorkItemAsync(TaskActivityWorkItem workItem) => Task.CompletedTask;

    public override Task<TaskActivityWorkItem> RenewWorkItemAsync(TaskActivityWorkItem workItem) =>
        this.service.RenewTaskActivityWorkItemLockAsync(workItem);
}

