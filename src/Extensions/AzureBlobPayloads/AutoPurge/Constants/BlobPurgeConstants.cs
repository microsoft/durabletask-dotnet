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
    /// The fixed orchestration instance ID for the auto-purge job in each task hub.
    /// </summary>
    public const string OrchestratorInstanceId = "BlobPurgeJob-__dt_blob_payload_autopurge__";

    /// <summary>
    /// The idempotent configuration event used to update batch size and wake the job.
    /// </summary>
    public const string SetBatchSizeEvent = "SetBatchSize";

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
}
