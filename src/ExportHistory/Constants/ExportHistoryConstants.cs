// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Constants used throughout the export history functionality.
/// </summary>
static class ExportHistoryConstants
{
    /// <summary>
    /// The prefix pattern used for generating export job orchestrator instance IDs.
    /// Format: "ExportJob-{jobId}"
    /// </summary>
    public const string OrchestratorInstanceIdPrefix = "ExportJob-";

    /// <summary>
    /// Generates an orchestrator instance ID for a given export job ID.
    /// </summary>
    /// <param name="jobId">The export job ID.</param>
    /// <returns>The orchestrator instance ID.</returns>
    public static string GetOrchestratorInstanceId(string jobId) => $"{OrchestratorInstanceIdPrefix}{jobId}";
}
