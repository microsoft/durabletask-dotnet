// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;

namespace Microsoft.DurableTask.Worker.OrchestrationServiceShim.Core;

/// <summary>
/// An <see cref="IOrchestrationService"/> that does not support entities.
/// </summary>
/// <remarks>
/// This is used to suppress entity support when <see cref="DurableTaskWorkerOptions.EnableEntitySupport"/> is false.
/// </remarks>
sealed class OrchestrationServiceNoEntities(IOrchestrationService service) : IOrchestrationService
{
    /// <inheritdoc/>
    public int TaskOrchestrationDispatcherCount => service.TaskOrchestrationDispatcherCount;

    /// <inheritdoc/>
    public int MaxConcurrentTaskOrchestrationWorkItems => service.MaxConcurrentTaskOrchestrationWorkItems;

    /// <inheritdoc/>
    public BehaviorOnContinueAsNew EventBehaviourForContinueAsNew => service.EventBehaviourForContinueAsNew;

    /// <inheritdoc/>
    public int TaskActivityDispatcherCount => service.TaskActivityDispatcherCount;

    /// <inheritdoc/>
    public int MaxConcurrentTaskActivityWorkItems => service.MaxConcurrentTaskActivityWorkItems;

    /// <inheritdoc/>
    public Task AbandonTaskActivityWorkItemAsync(TaskActivityWorkItem workItem)
        => service.AbandonTaskActivityWorkItemAsync(workItem);

    /// <inheritdoc/>
    public Task AbandonTaskOrchestrationWorkItemAsync(TaskOrchestrationWorkItem workItem)
        => service.AbandonTaskOrchestrationWorkItemAsync(workItem);

    /// <inheritdoc/>
    public Task CompleteTaskActivityWorkItemAsync(TaskActivityWorkItem workItem, TaskMessage responseMessage)
    => service.CompleteTaskActivityWorkItemAsync(workItem, responseMessage);

    /// <inheritdoc/>
    public Task CompleteTaskOrchestrationWorkItemAsync(
        TaskOrchestrationWorkItem workItem,
        OrchestrationRuntimeState newOrchestrationRuntimeState,
        IList<TaskMessage> outboundMessages,
        IList<TaskMessage> orchestratorMessages,
        IList<TaskMessage> timerMessages,
        TaskMessage continuedAsNewMessage,
        OrchestrationState orchestrationState)
        => service.CompleteTaskOrchestrationWorkItemAsync(
            workItem,
            newOrchestrationRuntimeState,
            outboundMessages,
            orchestratorMessages,
            timerMessages,
            continuedAsNewMessage,
            orchestrationState);

    /// <inheritdoc/>
    public Task CreateAsync() => service.CreateAsync();

    /// <inheritdoc/>
    public Task CreateAsync(bool recreateInstanceStore) => service.CreateAsync(recreateInstanceStore);

    /// <inheritdoc/>
    public Task CreateIfNotExistsAsync() => service.CreateIfNotExistsAsync();

    /// <inheritdoc/>
    public Task DeleteAsync() => service.DeleteAsync();

    /// <inheritdoc/>
    public Task DeleteAsync(bool deleteInstanceStore) => service.DeleteAsync(deleteInstanceStore);

    /// <inheritdoc/>
    public int GetDelayInSecondsAfterOnFetchException(Exception exception)
        => service.GetDelayInSecondsAfterOnFetchException(exception);

    /// <inheritdoc/>
    public int GetDelayInSecondsAfterOnProcessException(Exception exception)
    => service.GetDelayInSecondsAfterOnProcessException(exception);

    /// <inheritdoc/>
    public bool IsMaxMessageCountExceeded(int currentMessageCount, OrchestrationRuntimeState runtimeState)
        => service.IsMaxMessageCountExceeded(currentMessageCount, runtimeState);

    /// <inheritdoc/>
    public Task<TaskActivityWorkItem> LockNextTaskActivityWorkItem(
        TimeSpan receiveTimeout, CancellationToken cancellationToken)
        => service.LockNextTaskActivityWorkItem(receiveTimeout, cancellationToken);

    /// <inheritdoc/>
    public Task<TaskOrchestrationWorkItem> LockNextTaskOrchestrationWorkItemAsync(
        TimeSpan receiveTimeout, CancellationToken cancellationToken)
        => service.LockNextTaskOrchestrationWorkItemAsync(receiveTimeout, cancellationToken);

    /// <inheritdoc/>
    public Task ReleaseTaskOrchestrationWorkItemAsync(TaskOrchestrationWorkItem workItem)
        => service.ReleaseTaskOrchestrationWorkItemAsync(workItem);

    /// <inheritdoc/>
    public Task<TaskActivityWorkItem> RenewTaskActivityWorkItemLockAsync(TaskActivityWorkItem workItem)
        => service.RenewTaskActivityWorkItemLockAsync(workItem);

    /// <inheritdoc/>
    public Task RenewTaskOrchestrationWorkItemLockAsync(TaskOrchestrationWorkItem workItem)
        => service.RenewTaskOrchestrationWorkItemLockAsync(workItem);

    /// <inheritdoc/>
    public Task StartAsync() => service.StartAsync();

    /// <inheritdoc/>
    public Task StopAsync() => service.StopAsync();

    /// <inheritdoc/>
    public Task StopAsync(bool isForced) => service.StopAsync(isForced);
}
