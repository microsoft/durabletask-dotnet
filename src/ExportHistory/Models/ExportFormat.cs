// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

public record ExportFormat(
    string Kind = "jsonl",
    string SchemaVersion = "1.0");
