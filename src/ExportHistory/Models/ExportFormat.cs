// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Export format settings.
/// </summary>
/// <param name="Kind">The kind of export format.</param>
/// <param name="SchemaVersion">The schema version.</param>
public partial record ExportFormat(
    string Kind = "jsonl",
    string SchemaVersion = "1.0")
{
    /// <summary>
    /// Gets the default export format (jsonl with schema version 1.0).
    /// </summary>
    public static ExportFormat Default => new();
}
