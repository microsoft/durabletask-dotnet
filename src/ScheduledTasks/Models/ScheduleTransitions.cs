// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Manages valid state transitions for schedules.
/// </summary>
static class ScheduleTransitions
{
    /// <summary>
    /// Maps schedule states to their valid target states.
    /// </summary>
    static readonly Dictionary<ScheduleStatus, HashSet<ScheduleStatus>> ValidTransitions =
        new Dictionary<ScheduleStatus, HashSet<ScheduleStatus>>
        {
            { ScheduleStatus.Uninitialized, new HashSet<ScheduleStatus> { ScheduleStatus.Active } },
            { ScheduleStatus.Active, new HashSet<ScheduleStatus> { ScheduleStatus.Paused } },
            { ScheduleStatus.Paused, new HashSet<ScheduleStatus> { ScheduleStatus.Active } },
        };

    /// <summary>
    /// Attempts to get the valid target states for a given schedule state.
    /// </summary>
    /// <param name="from">The current schedule state.</param>
    /// <param name="validTargetStates">When this method returns, contains the valid target states if found; otherwise, an empty set.</param>
    /// <returns>True if valid transitions exist for the given state; otherwise, false.</returns>
    public static bool TryGetValidTransitions(ScheduleStatus from, out HashSet<ScheduleStatus> validTargetStates)
    {
        bool exists = ValidTransitions.TryGetValue(from, out HashSet<ScheduleStatus>? states);
        validTargetStates = states ?? new HashSet<ScheduleStatus>();
        return exists;
    }
}
