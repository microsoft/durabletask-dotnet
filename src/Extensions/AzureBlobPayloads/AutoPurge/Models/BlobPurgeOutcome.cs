// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// The outcome of attempting to delete a single externalized payload blob during an auto-purge cycle. The
/// orchestrator combines it with the tombstone's identity and revision to build the reported
/// <see cref="LargePayloadPurgeResult"/>.
/// </summary>
/// <remarks>
/// Carries the disposition alone, because that is the only outcome field the contract reports. Why an attempt
/// reached its disposition is logged by <see cref="DeleteExternalBlobActivity"/> at the point it is
/// classified, at higher fidelity than any value that could be carried here.
/// </remarks>
/// <param name="Disposition">Whether the row is resolved, should be retried, or must be quarantined.</param>
public sealed record BlobPurgeOutcome(LargePayloadPurgeDisposition Disposition);
