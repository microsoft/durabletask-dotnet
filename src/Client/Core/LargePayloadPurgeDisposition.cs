// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client;

/// <summary>
/// The outcome of a single large-payload blob deletion attempt. The split is by whether a failure can
/// self-heal. Mirrors the <c>LargePayloadPurgeDisposition</c> protobuf enum.
/// </summary>
public enum LargePayloadPurgeDisposition
{
    /// <summary>
    /// No disposition was specified.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// Terminal success. The blob was deleted or was already absent. The backend deletes the tombstone in
    /// both cases.
    /// </summary>
    Deleted = 1,

    /// <summary>
    /// The failure may self-heal, so the row stays pending and the backend sets the next attempt.
    /// </summary>
    Retry = 2,

    /// <summary>
    /// A deterministic failure or protocol violation that retrying can never fix. The backend preserves the
    /// evidence, alerts, and stops automatic retries.
    /// </summary>
    Quarantined = 3,
}
