// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

public record ExportFormat(
    string Kind = "jsonl",
    string SchemaVersion = "1.0");

public static class ExportFormatDefaults
{
    // Backing type to expose a singleton default instance while keeping record immutable defaults
    public static readonly ExportFormat Default = new();
}

// Maintain existing usage via type to keep call-sites clean
partial class ExportFormat
{
    public static ExportFormat Default => ExportFormatDefaults.Default;
}