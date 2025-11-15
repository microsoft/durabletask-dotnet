// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities;

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Orchestrator that executes operations on export job entities.
/// Calls the specified operation on the target entity and returns the result.
/// </summary>
[DurableTask]
public class ExecuteExportJobOperationOrchestrator : TaskOrchestrator<ExportJobOperationRequest, object>
{
    /// <inheritdoc/>
    public override async Task<object> RunAsync(TaskOrchestrationContext context, ExportJobOperationRequest input)
    {
        return await context.Entities.CallEntityAsync<object>(input.EntityId, input.OperationName, input.Input);
    }
}

/// <summary>
/// Request for executing a export job operation.
/// </summary>
/// <param name="EntityId">The ID of the entity to execute the operation on.</param>
/// <param name="OperationName">The name of the operation to execute.</param>
/// <param name="Input">Optional input for the operation.</param>
public record ExportJobOperationRequest(EntityInstanceId EntityId, string OperationName, object? Input = null);
