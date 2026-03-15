// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Export job modes.
/// </summary>
public enum ExportMode
{
    /// <summary>Exports a fixed window and completes.</summary>
    Batch = 1,

    /// <summary>Tails terminal instances continuously.</summary>
    Continuous = 2,
}
