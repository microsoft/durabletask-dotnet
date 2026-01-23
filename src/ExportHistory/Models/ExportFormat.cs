// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// The kind of export format.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExportFormatKind
{
    /// <summary>
    /// JSONL format (one history event per line, compressed with gzip).
    /// </summary>
    Jsonl,

    /// <summary>
    /// JSON format (array of history events, uncompressed).
    /// </summary>
    Json,
}

/// <summary>
/// Export format settings.
/// </summary>
/// <param name="Kind">The kind of export format.</param>
/// <param name="SchemaVersion">The schema version.</param>
public record ExportFormat(
    ExportFormatKind Kind = ExportFormatKind.Jsonl,
    string SchemaVersion = "1.0")
{
    /// <summary>
    /// Gets the default export format (jsonl with schema version 1.0).
    /// </summary>
    public static ExportFormat Default => new();
}
