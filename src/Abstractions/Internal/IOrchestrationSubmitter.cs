// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Internal;

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
    /// <param name="instanceId">
    /// The unique ID of the orchestration instance to schedule. If not specified, a randomGUID value is used.
    /// </param>
    /// <param name="input">
    /// The optional input to pass to the scheduled orchestration instance. This must be a serializable value.
    /// </param>
    /// <param name="startTime">
    /// The time when the orchestration instance should start executing. If not specified or if a date-time in the past
    /// is specified, the orchestration instance will be scheduled immediately.
    /// </param>
    /// <returns>
    /// A task that completes when the orchestration instance is successfully scheduled. The value of this task is
    /// the instance ID of the scheduled orchestration instance. If a non-null <paramref name="instanceId"/> parameter
    /// value was provided, the same value will be returned by the completed task.
    /// </returns>
    Task<string> ScheduleNewOrchestrationInstanceAsync(
        TaskName orchestratorName, string? instanceId = null, object? input = null, DateTimeOffset? startTime = null);
}
