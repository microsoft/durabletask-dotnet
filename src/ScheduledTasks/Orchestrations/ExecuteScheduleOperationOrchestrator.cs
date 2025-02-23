using Microsoft.DurableTask;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.ScheduledTasks;

[DurableTask("ExecuteScheduleOperation")]
public class ExecuteScheduleOperationOrchestrator : TaskOrchestrator<ScheduleOperationRequest, object?>
{
    public override async Task<object?> RunTask(TaskOrchestrationContext context, ScheduleOperationRequest input)
    {
        var logger = context.CreateReplaySafeLogger<ExecuteScheduleOperationOrchestrator>();
        logger.LogInformation("Starting schedule operation {Operation} for entity {EntityId}", input.OperationName, input.EntityId);

        try
        {
            var result = await context.Entities.CallEntityAsync(input.EntityId, input.OperationName, input.Input);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to execute schedule operation {Operation} for entity {EntityId}", input.OperationName, input.EntityId);
            throw;
        }
    }
}

public record ScheduleOperationRequest(EntityInstanceId EntityId, string OperationName, object? Input = null); 