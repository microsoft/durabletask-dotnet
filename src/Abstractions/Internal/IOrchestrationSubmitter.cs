// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Dapr.DurableTask;

namespace Dapr.DurableTask.Internal;

/// <summary>
/// This is an internal API that supports the DurableTask infrastructure and not subject to
/// the same compatibility standards as public APIs. It may be changed or removed without notice in
/// any release. You should only use it directly in your code with extreme caution and knowing that
/// doing so can result in application failures when updating to a new DurableTask release.
/// </summary>
/// <remarks>
/// <b>Do not</b> implement directly, instead use "DurableTaskClient" from the client package instead. This interface's
/// only purpose is to expose some methods to the abstraction layer for code generation.
/// </remarks>
public interface IOrchestrationSubmitter
{
    /// <summary>
    /// Schedules a new orchestration instance for execution.
    /// This is an internal API that supports the DurableTask infrastructure and not subject to
    /// the same compatibility standards as public APIs. It may be changed or removed without notice in
    /// any release. You should only use it directly in your code with extreme caution and knowing that
    /// doing so can result in application failures when updating to a new DurableTask release.
    /// </summary>
    /// <param name="orchestratorName">The name of the orchestrator to schedule.</param>
    /// <param name="input">
    /// The optional input to pass to the scheduled orchestration instance. This must be a serializable value.
    /// </param>
    /// <param name="options">The options to start the new orchestration with.</param>
    /// <param name="cancellation">
    /// The cancellation token. This only cancels enqueueing the new orchestration to the backend. Does not cancel the
    /// orchestration once enqueued.
    /// </param>
    /// <returns>
    /// A task that completes when the orchestration instance is successfully scheduled. The value of this task is
    /// the instance ID of the scheduled orchestration instance. If a non-null instance ID was provided via
    /// <paramref name="options" />, the same value will be returned by the completed task.
    /// </returns>
    Task<string> ScheduleNewOrchestrationInstanceAsync(
        TaskName orchestratorName,
        object? input = null,
        StartOrchestrationOptions? options = null,
        CancellationToken cancellation = default);
}
