// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities;

namespace Microsoft.DurableTask.ScheduledTasks;

// TODO: logging
// TODO: May need separate orchs, result is obj now

/// <summary>
/// Orchestrator that executes operations on schedule entities.
/// Calls the specified operation on the target entity and returns the result.
/// </summary>
[DurableTask("ExecuteScheduleOperation")]
public class ExecuteScheduleOperationOrchestrator : TaskOrchestrator<ScheduleOperationRequest, object>
{
    /// <inheritdoc/>
    public override async Task<object> RunAsync(TaskOrchestrationContext context, ScheduleOperationRequest input)
    {
        return await context.Entities.CallEntityAsync<object>(input.EntityId, input.OperationName, input.Input);
    }
}

/// <summary>
/// Request for executing a schedule operation.
/// </summary>
/// <param name="EntityId">The ID of the entity to execute the operation on.</param>
/// <param name="OperationName">The name of the operation to execute.</param>
/// <param name="Input">Optional input for the operation.</param>
public record ScheduleOperationRequest(EntityInstanceId EntityId, string OperationName, object? Input = null);
