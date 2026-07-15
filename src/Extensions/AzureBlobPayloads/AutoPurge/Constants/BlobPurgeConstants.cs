// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Constants used throughout the blob payload auto-purge functionality.
/// </summary>
static class BlobPurgeConstants
{
    /// <summary>
    /// The fixed, process-global job ID for the singleton blob payload auto-purge job. A single job drains
    /// tombstoned payloads for the whole scheduler, so the ID is hard-coded rather than caller-supplied.
    /// </summary>
    public const string JobId = "__dt_blob_payload_autopurge__";

    /// <summary>
    /// The default number of tombstoned payloads the auto-purge job requests from the backend per cycle,
    /// used whenever a batch size is not explicitly configured.
    /// </summary>
    public const int DefaultBatchSize = 500;

    /// <summary>
    /// The maximum batch size the auto-purge job may request per cycle. Mirrors the gRPC
    /// GetTombstonedPayloadsAsync contract, which rejects limits >= 1000.
    /// </summary>
    public const int MaxBatchSize = 999;

    /// <summary>
    /// The fixed instance ID of the client-to-entity bridge orchestration the starter schedules to ensure the
    /// singleton job. A fixed ID keeps racing client processes from creating duplicate bridge orchestrations.
    /// </summary>
    public const string StarterInstanceId = "BlobPurgeJobStarter-" + JobId;

    /// <summary>
    /// The prefix used for generating blob purge job orchestrator instance IDs. Format: "BlobPurgeJob-{jobId}".
    /// </summary>
    public const string OrchestratorInstanceIdPrefix = "BlobPurgeJob-";

    /// <summary>
    /// Generates an orchestrator instance ID for a given blob purge job ID.
    /// </summary>
    /// <param name="jobId">The blob purge job ID.</param>
    /// <returns>The orchestrator instance ID.</returns>
    public static string GetOrchestratorInstanceId(string jobId) => $"{OrchestratorInstanceIdPrefix}{jobId}";
}
