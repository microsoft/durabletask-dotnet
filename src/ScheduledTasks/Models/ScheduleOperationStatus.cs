// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Represents the current status of a schedule operation.
/// </summary>
public enum ScheduleOperationStatus
{
    /// <summary>
    /// The operation completed successfully.
    /// </summary>
    Succeeded,

    /// <summary>
    /// The operation failed.
    /// </summary>
    Failed,
}
