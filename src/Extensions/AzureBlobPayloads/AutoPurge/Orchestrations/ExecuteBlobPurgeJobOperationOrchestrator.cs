// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Orchestrator that executes a single operation on a blob purge job entity and returns the result. Used as
/// a client-to-entity bridge so clients can drive the entity through the orchestration surface.
/// </summary>
[DurableTask]
public class ExecuteBlobPurgeJobOperationOrchestrator
    : TaskOrchestrator<BlobPurgeJobOperationRequest, object>
{
    /// <inheritdoc/>
    public override async Task<object> RunAsync(
        TaskOrchestrationContext context, BlobPurgeJobOperationRequest input)
    {
        return await context.Entities.CallEntityAsync<object>(input.EntityId, input.OperationName, input.Input);
    }
}

/// <summary>
/// Request for executing a blob purge job entity operation.
/// </summary>
/// <param name="EntityId">The ID of the entity to execute the operation on.</param>
/// <param name="OperationName">The name of the operation to execute.</param>
/// <param name="Input">Optional input for the operation.</param>
public record BlobPurgeJobOperationRequest(EntityInstanceId EntityId, string OperationName, object? Input = null);
