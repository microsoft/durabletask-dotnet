// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Manages valid state transitions for schedules.
/// </summary>
static class ScheduleTransitions
{
    // define valid transitions for create schedule
    static readonly Dictionary<ScheduleStatus, HashSet<ScheduleStatus>> CreateScheduleStatusTransitions =
        new Dictionary<ScheduleStatus, HashSet<ScheduleStatus>>
        {
            { ScheduleStatus.Uninitialized, new HashSet<ScheduleStatus> { ScheduleStatus.Active } },
        };

    // define valid transitions for update schedule
    static readonly Dictionary<ScheduleStatus, HashSet<ScheduleStatus>> UpdateScheduleStatusTransitions =
        new Dictionary<ScheduleStatus, HashSet<ScheduleStatus>>
        {
            { ScheduleStatus.Active, new HashSet<ScheduleStatus> { ScheduleStatus.Active } },
            { ScheduleStatus.Paused, new HashSet<ScheduleStatus> { ScheduleStatus.Paused } },
        };

    // define valid transitions for pause schedule
    static readonly Dictionary<ScheduleStatus, HashSet<ScheduleStatus>> PauseScheduleStatusTransitions =
        new Dictionary<ScheduleStatus, HashSet<ScheduleStatus>>
        {
            { ScheduleStatus.Active, new HashSet<ScheduleStatus> { ScheduleStatus.Paused } },
        };

    // define valid transitions for resume schedule
    static readonly Dictionary<ScheduleStatus, HashSet<ScheduleStatus>> ResumeScheduleStatusTransitions =
        new Dictionary<ScheduleStatus, HashSet<ScheduleStatus>>
        {
            { ScheduleStatus.Paused, new HashSet<ScheduleStatus> { ScheduleStatus.Active } },
        };

    /// <summary>
    /// Attempts to get the valid target states for a given schedule state and operation.
    /// </summary>
    /// <param name="operationName">The name of the operation being performed.</param>
    /// <param name="from">The current schedule state.</param>
    /// <param name="validTargetStates">When this method returns, contains the valid target states if found; otherwise, an empty set.</param>
    /// <returns>True if valid transitions exist for the given state and operation; otherwise, false.</returns>
    public static bool TryGetValidTransitions(string operationName, ScheduleStatus from, out HashSet<ScheduleStatus> validTargetStates)
    {
        Dictionary<ScheduleStatus, HashSet<ScheduleStatus>> transitionMap = operationName switch
        {
            nameof(Schedule.CreateSchedule) => CreateScheduleStatusTransitions,
            nameof(Schedule.UpdateSchedule) => UpdateScheduleStatusTransitions,
            nameof(Schedule.PauseSchedule) => PauseScheduleStatusTransitions,
            nameof(Schedule.ResumeSchedule) => ResumeScheduleStatusTransitions,
            _ => new Dictionary<ScheduleStatus, HashSet<ScheduleStatus>>(),
        };

        bool exists = transitionMap.TryGetValue(from, out HashSet<ScheduleStatus>? states);
        validTargetStates = states ?? new HashSet<ScheduleStatus>();
        return exists;
    }
}
