// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

public enum ScheduleStatus
{
    Uninitialized, // Schedule has not been created
    Active,       // Schedule is active and running
    Paused,       // Schedule is paused
}
