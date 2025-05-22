// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Dapr.DurableTask.ScheduledTasks;

/// <summary>
/// Manages valid state transitions for schedules.
/// </summary>
static class ScheduleTransitions
{
    /// <summary>
    /// Checks if a transition to the target state is valid for a given schedule state and operation.
    /// </summary>
    /// <param name="operationName">The name of the operation being performed.</param>
    /// <param name="from">The current schedule state.</param>
    /// <param name="targetState">The target state to transition to.</param>
    /// <returns>True if the transition is valid; otherwise, false.</returns>
    public static bool IsValidTransition(string operationName, ScheduleStatus from, ScheduleStatus targetState)
    {
        return operationName switch
        {
            nameof(Schedule.CreateSchedule) => from switch
            {
                ScheduleStatus.Uninitialized when targetState == ScheduleStatus.Active => true,
                ScheduleStatus.Active when targetState == ScheduleStatus.Active => true,
                ScheduleStatus.Paused when targetState == ScheduleStatus.Active => true,
                _ => false,
            },
            nameof(Schedule.UpdateSchedule) => from switch
            {
                ScheduleStatus.Active when targetState == ScheduleStatus.Active => true,
                ScheduleStatus.Paused when targetState == ScheduleStatus.Paused => true,
                _ => false,
            },
            nameof(Schedule.PauseSchedule) => from switch
            {
                ScheduleStatus.Active when targetState == ScheduleStatus.Paused => true,
                _ => false,
            },
            nameof(Schedule.ResumeSchedule) => from switch
            {
                ScheduleStatus.Paused when targetState == ScheduleStatus.Active => true,
                _ => false,
            },
            _ => false,
        };
    }
}
