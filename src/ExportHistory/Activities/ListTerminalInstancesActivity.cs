// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Activity that lists terminal orchestration instances using the configured filters and watermark.
/// </summary>
[DurableTask]
public class ListTerminalInstancesActivity : TaskActivity<ExportJobState, InstancePage>
{
    /// <inheritdoc/>
    public override Task<InstancePage> RunAsync(TaskActivityContext context, ExportJobState state)
    {
        // Placeholder to compile; real implementation will use client querying and filters.
        return Task.FromResult(new InstancePage());
    }
}

/// <summary>
/// A page of instances for export.
/// </summary>
public sealed class InstancePage
{
    public List<string> InstanceIds { get; set; } = new();
    public ExportWatermark? NextWatermark { get; set; }
}


