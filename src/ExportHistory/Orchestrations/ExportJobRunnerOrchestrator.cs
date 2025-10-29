// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities;

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Orchestrator that runs a history export job: pages terminal instances, exports histories, and checkpoints.
/// </summary>
[DurableTask]
public class ExportJobRunnerOrchestrator : TaskOrchestrator<ExportJobRunRequest, object?>
{
    /// <inheritdoc/>
    public override async Task<object?> RunAsync(TaskOrchestrationContext context, ExportJobRunRequest input)
    {
        // Minimal scaffold: read state to validate token and status, then no-op for now to compile.
        ExportJobState state = await context.Entities.CallEntityAsync<ExportJobState>(input.JobEntityId, "Get");
        if (state == null || state.Status != ExportJobStatus.Running || state.ExecutionToken != input.ExecutionToken)
        {
            return null;
        }

        // In full implementation, loop pages -> fan-out export activities -> checkpoint -> continue-as-new.
        return null;
    }
}


