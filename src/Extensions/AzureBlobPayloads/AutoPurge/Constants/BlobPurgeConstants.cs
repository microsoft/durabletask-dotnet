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
