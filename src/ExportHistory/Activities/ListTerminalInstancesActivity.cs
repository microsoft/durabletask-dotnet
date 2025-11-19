// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Input for listing terminal instances activity.
/// </summary>
public sealed record ListTerminalInstancesRequest(
    DateTimeOffset CompletedTimeFrom,
    DateTimeOffset? CompletedTimeTo,
    IEnumerable<OrchestrationRuntimeStatus>? RuntimeStatus,
    string? LastInstanceKey,
    int MaxInstancesPerBatch = 100);

/// <summary>
/// Activity that lists terminal orchestration instances using the configured filters and checkpoint.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ListTerminalInstancesActivity"/> class.
/// </remarks>
[DurableTask]
public class ListTerminalInstancesActivity(
    DurableTaskClient client,
    ILogger<ListTerminalInstancesActivity> logger) : TaskActivity<ListTerminalInstancesRequest, InstancePage>
{
    readonly DurableTaskClient client = Check.NotNull(client, nameof(client));
    readonly ILogger<ListTerminalInstancesActivity> logger = Check.NotNull(logger, nameof(logger));

    /// <inheritdoc/>
    public override async Task<InstancePage> RunAsync(TaskActivityContext context, ListTerminalInstancesRequest input)
    {
        Check.NotNull(input, nameof(input));

        try
        {
            // Try to use ListInstanceIds endpoint first (available in gRPC client)
            Page<string> page = await this.client.ListInstanceIdsAsync(
                    runtimeStatus: input.RuntimeStatus,
                    completedTimeFrom: input.CompletedTimeFrom,
                    completedTimeTo: input.CompletedTimeTo,
                    pageSize: input.MaxInstancesPerBatch,
                    lastInstanceKey: input.LastInstanceKey,
                    cancellation: CancellationToken.None);

            this.logger.LogInformation(
                "ListTerminalInstancesActivity returned {Count} instance IDs using ListInstanceIds",
                page.Values.Count);

            return new InstancePage(page.Values.ToList(), new ExportCheckpoint(page.ContinuationToken));
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "ListTerminalInstancesActivity failed");
            throw;
        }
    }
}

/// <summary>
/// A page of instances for export.
/// </summary>
public sealed class InstancePage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InstancePage"/> class.
    /// </summary>
    /// <param name="instanceIds">The list of instance IDs.</param>
    /// <param name="nextCheckpoint">The next checkpoint for pagination.</param>
    public InstancePage(List<string> instanceIds, ExportCheckpoint nextCheckpoint)
    {
        this.InstanceIds = instanceIds;
        this.NextCheckpoint = nextCheckpoint;
    }

    /// <summary>
    /// Gets or sets the list of instance IDs.
    /// </summary>
    public List<string> InstanceIds { get; set; } = [];

    /// <summary>
    /// Gets or sets the next checkpoint for pagination.
    /// </summary>
    public ExportCheckpoint NextCheckpoint { get; set; }
}
