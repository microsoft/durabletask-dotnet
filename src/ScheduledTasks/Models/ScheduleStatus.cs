// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

// TODO: Find whether it is possible to remove uninitialized, have to ensure atomicity of creation if possible

/// <summary>
/// Represents the current status of a schedule.
/// </summary>
public enum ScheduleStatus
{
    /// <summary>
    /// Schedule has not been created.
    /// </summary>
    Uninitialized,

    /// <summary>
    /// Schedule is active and running.
    /// </summary>
    Active,

    /// <summary>
    /// Schedule is paused.
    /// </summary>
    Paused,
}
