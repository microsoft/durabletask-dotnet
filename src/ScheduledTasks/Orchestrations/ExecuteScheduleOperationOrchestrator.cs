// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.ScheduledTasks;

// TODO: May need separate orchs, result is obj now

/// <summary>
/// Orchestrator that executes operations on schedule entities.
/// Calls the specified operation on the target entity and returns the result.
/// </summary>
[DurableTask]
public class ExecuteScheduleOperationOrchestrator : TaskOrchestrator<ScheduleOperationRequest, object>
{
    /// <inheritdoc/>
    public override async Task<object> RunAsync(TaskOrchestrationContext context, ScheduleOperationRequest input)
    {
        ILogger logger = context.CreateReplaySafeLogger<ExecuteScheduleOperationOrchestrator>();
        string scheduleId = input.EntityId.Key;

        logger.ScheduleOperationInfo(scheduleId, input.OperationName, "Executing schedule operation via orchestrator");

        try
        {
            object result = await context.Entities.CallEntityAsync<object>(input.EntityId, input.OperationName, input.Input);
            logger.ScheduleOperationInfo(scheduleId, input.OperationName, "Schedule operation completed successfully");
            return result;
        }
        catch (Exception ex)
        {
            logger.ScheduleOperationError(scheduleId, input.OperationName, "Failed to execute schedule operation via orchestrator", ex);
            throw;
        }
    }
}

/// <summary>
/// Request for executing a schedule operation.
/// </summary>
/// <param name="EntityId">The ID of the entity to execute the operation on.</param>
/// <param name="OperationName">The name of the operation to execute.</param>
/// <param name="Input">Optional input for the operation.</param>
public record ScheduleOperationRequest(EntityInstanceId EntityId, string OperationName, object? Input = null);
