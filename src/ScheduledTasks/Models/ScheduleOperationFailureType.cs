// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Represents the type of failure that occurred during a schedule operation.
/// </summary>
public enum ScheduleOperationFailureType
{
    /// <summary>
    /// The operation failed due to an invalid operation being attempted.
    /// </summary>
    InvalidOperation,

    /// <summary>
    /// The operation failed due to an invalid state transition.
    /// </summary>
    InvalidStateTransition,

    /// <summary>
    /// The operation failed due to validation errors.
    /// </summary>
    ValidationError,

    /// <summary>
    /// The operation failed due to an internal server error.
    /// </summary>
    InternalError,
}
