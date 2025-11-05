// Licensed under the MIT License.
// Copyright (c) Microsoft Corporation.

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Checkpoint information used to resume export.
/// </summary>
public sealed record ExportCheckpoint(string? ContinuationToken = null);