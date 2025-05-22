// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Dapr.DurableTask;

namespace Dapr.DurableTask.Entities;

/// <summary>
/// Exception that gets thrown when an entity operation fails with an unhandled exception.
/// </summary>
/// <remarks>
/// Detailed information associated with a particular operation failure, including exception details, can be found in the
/// <see cref="FailureDetails"/> property.
/// </remarks>
public sealed class EntityOperationFailedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EntityOperationFailedException"/> class.
    /// </summary>
    /// <param name="operationName">The operation name.</param>
    /// <param name="entityId">The entity ID.</param>
    /// <param name="failureDetails">The failure details.</param>
    public EntityOperationFailedException(EntityInstanceId entityId, string operationName, TaskFailureDetails failureDetails)
        : base(GetExceptionMessage(operationName, entityId, failureDetails))
    {
        this.EntityId = entityId;
        this.OperationName = operationName;
        this.FailureDetails = failureDetails;
    }

    /// <summary>
    /// Gets the ID of the entity.
    /// </summary>
    public EntityInstanceId EntityId { get; }

    /// <summary>
    /// Gets the name of the operation.
    /// </summary>
    public string OperationName { get; }

     /// <summary>
    /// Gets the details of the task failure, including exception information.
    /// </summary>
    public TaskFailureDetails FailureDetails { get; }

    static string GetExceptionMessage(string operationName, EntityInstanceId entityId, TaskFailureDetails failureDetails)
    {
        return $"Operation '{operationName}' of entity '{entityId}' failed: {failureDetails.ErrorMessage}";
    }
}
