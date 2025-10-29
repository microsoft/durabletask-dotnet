// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Grpc;
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

            // Check if the client is a gRPC client that supports ListTerminalInstances
            if (client is not GrpcDurableTaskClient grpcClient)
            {
                throw new NotSupportedException(
                    $"ListTerminalInstancesActivity requires a GrpcDurableTaskClient, but got {client.GetType().Name}");
            }

            // Call the gRPC endpoint to list terminal instances
            (IReadOnlyList<string> instanceIds, string? nextContinuationToken) = await grpcClient.ListTerminalInstancesAsync(
                createdFrom: request.CreatedTimeFrom,
                createdTo: request.CreatedTimeTo,
                statuses: request.RuntimeStatus,
                pageSize: request.MaxInstancesPerBatch,
                continuationToken: request.ContinuationToken,
                cancellation: CancellationToken.None);

            this.logger.LogInformation(
                "ListTerminalInstancesActivity returned {Count} instance IDs",
                instanceIds.Count);

            // Create next checkpoint if we have a continuation token
            ExportCheckpoint? nextCheckpoint = null;
            if (!string.IsNullOrEmpty(nextContinuationToken) && instanceIds.Count > 0)
            {
                // Use the continuation token from the response (which is the last instanceId)
                string lastInstanceId = nextContinuationToken;
                nextCheckpoint = new ExportCheckpoint
                {
                    ContinuationToken = lastInstanceId,
                    LastInstanceIdProcessed = lastInstanceId,
                    // Note: LastTerminalTimeProcessed would require querying the instance, which we skip for performance
                };
            }

            return new InstancePage
            {
                InstanceIds = instanceIds.ToList(),
                NextCheckpoint = nextCheckpoint,
            };
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
    public ExportCheckpoint? NextCheckpoint { get; set; }
}


