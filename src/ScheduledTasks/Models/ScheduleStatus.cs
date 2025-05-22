// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Dapr.DurableTask.ScheduledTasks;

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
