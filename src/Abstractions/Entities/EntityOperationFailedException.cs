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
/// <param name="operationName">The operation name.</param>
/// <param name="entityId">The entity ID.</param>
/// <param name="failureDetails">The failure details.</param>
public sealed class EntityOperationFailedException(
    EntityInstanceId entityId, string operationName, TaskFailureDetails failureDetails)
    : Exception(GetExceptionMessage(operationName, entityId, failureDetails))
{
    /// <summary>
    /// Gets the ID of the entity.
    /// </summary>
    public EntityInstanceId EntityId { get; } = entityId;

    /// <summary>
    /// Gets the name of the operation.
    /// </summary>
    public string OperationName { get; } = operationName;

    /// <summary>
    /// Gets the details of the task failure, including exception information.
    /// </summary>
    public TaskFailureDetails FailureDetails { get; } = failureDetails;

    static string GetExceptionMessage(string operationName, EntityInstanceId entityId, TaskFailureDetails failureDetails)
    {
        return $"Operation '{operationName}' of entity '{entityId}' failed: {failureDetails.ErrorMessage}";
    }
}
