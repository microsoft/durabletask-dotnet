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
public sealed class OperationFailedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OperationFailedException"/> class.
    /// </summary>
    /// <param name="operationName">The operation name.</param>
    /// <param name="entityId">The entity ID.</param>
    /// <param name="errorContext">The context in which the error was caught.</param>
    /// <param name="failureDetails">The failure details.</param>
    public OperationFailedException(EntityInstanceId entityId, string operationName, string errorContext, TaskFailureDetails failureDetails)
        : base(GetExceptionMessage(operationName, entityId, errorContext, failureDetails))
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

    static string GetExceptionMessage(string operationName, EntityInstanceId entityId, string errorContext, TaskFailureDetails failureDetails)
    {
        return $"Operation '{operationName}' of entity '{entityId}' failed: {errorContext}: {failureDetails.ErrorMessage}";
    }
}
