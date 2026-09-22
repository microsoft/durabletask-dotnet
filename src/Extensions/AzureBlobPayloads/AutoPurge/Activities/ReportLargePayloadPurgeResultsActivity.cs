// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using static Microsoft.DurableTask.Protobuf.LargePayloads.LargePayloadPurge;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Activity that reports the outcome of every attempted blob deletion to the backend, so it can delete the
/// resolved tombstones, reschedule the retryable ones, and quarantine the rest. Every attempted row is
/// reported, not only the successful ones: the backend owns retry scheduling, so a row it hears nothing about
/// would simply be re-served unchanged on the next cycle.
/// </summary>
/// <param name="client">The large-payload purge service client used to report purge results to the backend.</param>
/// <param name="logger">The logger instance.</param>
/// <remarks>
/// Infrastructure integration API for alternate .NET hosts. The supplied client must be bound to this
/// worker's authenticated task hub. Its transport lifetime remains owned by the host.
/// </remarks>
[DurableTask]
public sealed class ReportLargePayloadPurgeResultsActivity(
    ILargePayloadPurgeClient client,
    ILogger<ReportLargePayloadPurgeResultsActivity> logger)
    : TaskActivity<List<LargePayloadPurgeResult>, object?>
{
    readonly ILargePayloadPurgeClient client = Check.NotNull(client);
    readonly ILogger<ReportLargePayloadPurgeResultsActivity> logger = Check.NotNull(logger);

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportLargePayloadPurgeResultsActivity"/> class using the worker's transport.
    /// </summary>
    /// <param name="client">The worker's purge client.</param>
    /// <param name="logger">The activity logger.</param>
    internal ReportLargePayloadPurgeResultsActivity(
        LargePayloadPurgeClient client, ILogger<ReportLargePayloadPurgeResultsActivity> logger)
        : this(new GrpcLargePayloadPurgeClient(client), logger)
    {
    }

    /// <summary>
    /// Gets or sets the timeout for one backend RPC attempt.
    /// </summary>
    /// <remarks>
    /// This does not bound stop latency from activity scheduling, retries, or calls made by older workers.
    /// </remarks>
    internal TimeSpan RpcTimeout { get; set; } = TimeSpan.FromSeconds(BlobPurgeConstants.RpcTimeoutSeconds);

    /// <inheritdoc/>
    public override async Task<object?> RunAsync(
        TaskActivityContext context, List<LargePayloadPurgeResult> input)
    {
        if (input is null || input.Count == 0)
        {
            return null;
        }

        try
        {
            await this.client.ReportLargePayloadPurgeResultsAsync(input, DateTime.UtcNow.Add(this.RpcTimeout));
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled)
        {
            throw new OperationCanceledException(
                "The ReportLargePayloadPurgeResultsAsync operation was canceled.", e);
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Unimplemented)
        {
            // Mixed-rollout guard: an older backend build (or a stale local emulator image) does not implement
            // this RPC. The orchestrator waits for an explicit enable event instead of retrying indefinitely.
            throw new NotImplementedException(
                "The Durable Task backend does not implement the ReportLargePayloadPurgeResults RPC required " +
                "for large-payload auto-purge. The runner will wait for an explicit enable event. " +
                "Upgrade the backend (or re-pull " +
                "'mcr.microsoft.com/dts/dts-emulator'), then call SetLargePayloadAutoPurgeAsync(true, ...) " +
                $"again to re-enable it. Backend detail: {e.Status.Detail}",
                e);
        }

        this.logger.BlobPurgeReportedResults(input.Count);
        return null;
    }
}
