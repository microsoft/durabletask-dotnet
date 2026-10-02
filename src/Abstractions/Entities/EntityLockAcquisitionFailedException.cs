// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Entities;

/// <summary>
/// Exception that gets thrown when an entity operation fails with an unhandled exception.
/// </summary>
/// <remarks>
/// Detailed information associated with a particular operation failure, including exception details, can be found in the
/// <see cref="FailureDetails"/> property.
/// </remarks>
public sealed class EntityLockAcquisitionFailedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EntityLockAcquisitionFailedException"/> class.
    /// </summary>
    /// <param name="entityIds">The entity IDs being locked.</param>
    /// <param name="failureDetails">The failure details.</param>
    public EntityLockAcquisitionFailedException(IEnumerable<EntityInstanceId> entityIds, TaskFailureDetails failureDetails)
        : base(GetExceptionMessage(entityIds, failureDetails))
    {
        this.EntityIds = entityIds;
        this.FailureDetails = failureDetails;
    }

    /// <summary>
    /// Gets the IDs of the entities for which the lock request was issued.
    /// </summary>
    public IEnumerable<EntityInstanceId> EntityIds { get; }

    /// <summary>
    /// Gets the details of the task failure, including exception information.
    /// </summary>
    public TaskFailureDetails FailureDetails { get; }

    static string GetExceptionMessage(IEnumerable<EntityInstanceId> entityIds, TaskFailureDetails failureDetails)
    {
        return $"Acquisition of locks for entities '{string.Join(",", entityIds)}' failed: {failureDetails.ErrorMessage}";
    }
}
