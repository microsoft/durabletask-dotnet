// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Input for listing terminal instances activity.
/// </summary>
public sealed record ListTerminalInstancesRequest(
    DateTimeOffset CreatedTimeFrom,
    DateTimeOffset? CreatedTimeTo,
    IEnumerable<OrchestrationRuntimeStatus>? RuntimeStatus,
    string? ContinuationToken,
    int MaxInstancesPerBatch = 100);

/// <summary>
/// Activity that lists terminal orchestration instances using the configured filters and checkpoint.
/// </summary>
[DurableTask]
public class ListTerminalInstancesActivity : TaskActivity<ListTerminalInstancesRequest, InstancePage>
{
    readonly IDurableTaskClientProvider clientProvider;
    readonly ILogger<ListTerminalInstancesActivity> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListTerminalInstancesActivity"/> class.
    /// </summary>
    public ListTerminalInstancesActivity(
        IDurableTaskClientProvider clientProvider,
        ILogger<ListTerminalInstancesActivity> logger)
    {
        this.clientProvider = Check.NotNull(clientProvider, nameof(clientProvider));
        this.logger = Check.NotNull(logger, nameof(logger));
    }

    /// <inheritdoc/>
    public override async Task<InstancePage> RunAsync(TaskActivityContext context, ListTerminalInstancesRequest request)
    {
        Check.NotNull(request, nameof(request));

        try
        {
            DurableTaskClient client = this.clientProvider.GetClient();

            // Use QueryInstances to fetch terminal instances and project to IDs
            OrchestrationQuery query = new(
                CreatedFrom: request.CreatedTimeFrom,
                CreatedTo: request.CreatedTimeTo,
                Statuses: request.RuntimeStatus,
                PageSize: request.MaxInstancesPerBatch,
                FetchInputsAndOutputs: false,
                ContinuationToken: request.ContinuationToken);

            List<string> instanceIds = new();
            string? nextContinuationToken = null;

            await foreach (Page<OrchestrationMetadata> page in client
                .GetAllInstancesAsync(query)
                .AsPages()
                .WithCancellation(CancellationToken.None))
            {
                instanceIds.AddRange(page.Values.Select(v => v.InstanceId));
                nextContinuationToken = page.ContinuationToken;
                break;
            }

            this.logger.LogInformation(
                "ListTerminalInstancesActivity returned {Count} instance IDs",
                instanceIds.Count);

            return new InstancePage(instanceIds, new ExportCheckpoint(nextContinuationToken));
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
    public List<string> InstanceIds { get; set; } = new();
    public ExportCheckpoint NextCheckpoint { get; set; }
}


