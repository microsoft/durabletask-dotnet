// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Constants used throughout the blob payload auto-purge functionality.
/// </summary>
static class BlobPurgeConstants
{
    /// <summary>
    /// The fixed entity key for the one logical blob payload auto-purge job in each task hub.
    /// </summary>
    public const string JobId = "__dt_blob_payload_autopurge__";

    /// <summary>
    /// The default number of tombstoned payloads the auto-purge job requests from the backend per cycle,
    /// used whenever a batch size is not passed explicitly.
    /// </summary>
    public const int DefaultBatchSize = 500;

    /// <summary>
    /// The maximum batch size the auto-purge job may request per cycle. Delegates to
    /// <see cref="LargePayloadTombstone.MaxRequestLimit"/>, the single authority for the gRPC
    /// GetLargePayloadTombstones contract bound, so the two cannot drift.
    /// </summary>
    public const int MaxBatchSize = LargePayloadTombstone.MaxRequestLimit;

    /// <summary>
    /// The maximum duration of an individual fetch or report RPC attempt.
    /// </summary>
    public const int RpcTimeoutSeconds = 60;

    /// <summary>
    /// The prefix for legacy and generation-specific purge runner instance IDs.
    /// </summary>
    public const string OrchestratorInstanceIdPrefix = "BlobPurgeJob-";

    /// <summary>
    /// Generates an orchestrator instance ID for a given blob purge job ID.
    /// </summary>
    /// <param name="jobId">The blob purge job ID.</param>
    /// <param name="generation">The activation generation, or null for the legacy fixed ID.</param>
    /// <returns>The orchestrator instance ID.</returns>
    public static string GetOrchestratorInstanceId(string jobId, string? generation = null) =>
        generation is null
            ? $"{OrchestratorInstanceIdPrefix}{jobId}"
            : $"{OrchestratorInstanceIdPrefix}{jobId}-{generation}";
}
