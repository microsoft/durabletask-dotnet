// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// The outcome of attempting to delete a single externalized payload blob during an auto-purge cycle. The
/// orchestrator combines it with the tombstone's identity and revision to build the reported
/// <see cref="LargePayloadPurgeResult"/>.
/// </summary>
/// <param name="Disposition">Whether the row is resolved, should be retried, or must be quarantined.</param>
/// <param name="Reason">The stable reason code explaining the disposition.</param>
/// <param name="StorageErrorCode">
/// An optional bounded, sanitized storage status or error code for diagnostics. Never a token or raw
/// exception text.
/// </param>
public sealed record BlobPurgeOutcome(
    LargePayloadPurgeDisposition Disposition,
    LargePayloadPurgeReason Reason,
    string? StorageErrorCode = null);
