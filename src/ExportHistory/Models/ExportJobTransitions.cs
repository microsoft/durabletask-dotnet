// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Manages valid state transitions for export jobs.
/// </summary>
static class ExportJobTransitions
{
    /// <summary>
    /// Checks if a transition to the target state is valid for a given export job state and operation.
    /// </summary>
    /// <param name="operationName">The name of the operation being performed.</param>
    /// <param name="from">The current export job state.</param>
    /// <param name="targetState">The target state to transition to.</param>
    /// <returns>True if the transition is valid; otherwise, false.</returns>
    public static bool IsValidTransition(string operationName, ExportJobStatus from, ExportJobStatus targetState)
    {
        return operationName switch
        {
            nameof(ExportJob.Create) => from switch
            {
                ExportJobStatus.Uninitialized when targetState == ExportJobStatus.Active => true,
                ExportJobStatus.Failed when targetState == ExportJobStatus.Active => true,
                ExportJobStatus.Completed when targetState == ExportJobStatus.Active => true,
                _ => false,
            },
            nameof(ExportJob.MarkAsCompleted) => from switch
            {
                ExportJobStatus.Active when targetState == ExportJobStatus.Completed => true,
                _ => false,
            },
            nameof(ExportJob.MarkAsFailed) => from switch
            {
                ExportJobStatus.Active when targetState == ExportJobStatus.Failed => true,
                _ => false,
            },
            _ => false,
        };
    }
}

