// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

static class ScheduleTransitions
{
    static readonly Dictionary<ScheduleStatus, HashSet<ScheduleStatus>> ValidTransitions =
        new Dictionary<ScheduleStatus, HashSet<ScheduleStatus>>
        {
            { ScheduleStatus.Uninitialized, new HashSet<ScheduleStatus> { ScheduleStatus.Active } },
            { ScheduleStatus.Active, new HashSet<ScheduleStatus> { ScheduleStatus.Paused } },
            { ScheduleStatus.Paused, new HashSet<ScheduleStatus> { ScheduleStatus.Active } },
        };

    public static bool TryGetValidTransitions(ScheduleStatus from, out HashSet<ScheduleStatus> validTargetStates)
    {
        return ValidTransitions.TryGetValue(from, out validTargetStates);
    }
}
