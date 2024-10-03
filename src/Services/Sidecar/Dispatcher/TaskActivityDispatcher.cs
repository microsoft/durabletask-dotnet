// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.History;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Sidecar.Dispatcher;

class TaskActivityDispatcher(
    ILogger log,
    ITrafficSignal trafficSignal,
    IOrchestrationService service,
    ITaskExecutor taskExecutor) : WorkItemDispatcher<TaskActivityWorkItem>(log, trafficSignal)
{
    readonly IOrchestrationService service = service;
    readonly ITaskExecutor taskExecutor = taskExecutor;

    public override int MaxWorkItems => this.service.MaxConcurrentTaskActivityWorkItems;

    public override Task AbandonWorkItemAsync(TaskActivityWorkItem workItem)
    {
        return this.service.AbandonTaskActivityWorkItemAsync(workItem);
    }

    public override Task<TaskActivityWorkItem?> FetchWorkItemAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return this.service.LockNextTaskActivityWorkItem(timeout, cancellationToken);
    }

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

    public override int GetDelayInSecondsOnFetchException(Exception ex)
    {
        return this.service.GetDelayInSecondsAfterOnFetchException(ex);
    }

    public override string GetWorkItemId(TaskActivityWorkItem workItem)
    {
        return workItem.Id;
    }

    // No-op
    public override Task ReleaseWorkItemAsync(TaskActivityWorkItem workItem)
    {
        return Task.CompletedTask;
    }

    public override Task<TaskActivityWorkItem> RenewWorkItemAsync(TaskActivityWorkItem workItem)
    {
        return this.service.RenewTaskActivityWorkItemLockAsync(workItem);
    }
}