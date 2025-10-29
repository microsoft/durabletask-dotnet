// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Activity that exports a single instance's history to the configured blob destination.
/// </summary>
[DurableTask]
public class ExportInstanceHistoryActivity : TaskActivity<ExportRequest, ExportResult>
{
    /// <inheritdoc/>
    public override Task<ExportResult> RunAsync(TaskActivityContext context, ExportRequest input)
    {
        // Placeholder to compile; real implementation will stream history and write NDJSON.gz.
        return Task.FromResult(new ExportResult { InstanceId = input.InstanceId, Success = true });
    }
}

/// <summary>
/// Export request for a specific orchestration instance.
/// </summary>
public sealed class ExportRequest
{
    public string InstanceId { get; set; } = string.Empty;
}

/// <summary>
/// Export result.
/// </summary>
public sealed class ExportResult
{
    public string InstanceId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Error { get; set; }
}


