// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Checkpoint information used to resume export.
/// </summary>
public sealed record ExportCheckpoint(string? LastInstanceKey = null);
