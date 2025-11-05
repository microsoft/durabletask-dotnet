// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

public partial record ExportFormat(
    string Kind = "jsonl",
    string SchemaVersion = "1.0")
{
    /// <summary>
    /// Gets the default export format (jsonl with schema version 1.0).
    /// </summary>
    public static ExportFormat Default => new();
}
