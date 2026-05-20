// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Proto = Microsoft.DurableTask.Protobuf.Serverless;

namespace Microsoft.DurableTask.Client.AzureManaged;

/// <summary>
/// Client for DTS serverless activity management operations.
/// </summary>
public sealed class ServerlessActivitiesClient
{
    readonly Proto.ServerlessActivities.ServerlessActivitiesClient client;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerlessActivitiesClient"/> class.
    /// </summary>
    /// <param name="client">The generated gRPC client used to call DTS serverless management operations.</param>
    internal ServerlessActivitiesClient(Proto.ServerlessActivities.ServerlessActivitiesClient client)
    {
        this.client = client;
    }

    /// <summary>
    /// Lists DTS-managed sandboxes for a serverless activity worker profile.
    /// </summary>
    /// <param name="workerProfileId">The worker profile ID to list sandboxes for.</param>
    /// <param name="cancellation">The cancellation token used to cancel the request.</param>
    /// <returns>The sandboxes currently known to DTS for the worker profile.</returns>
    public Task<IReadOnlyList<ServerlessSandboxInfo>> ListServerlessActivitySandboxesAsync(
        string workerProfileId,
        CancellationToken cancellation = default)
        => this.client.ListServerlessActivitySandboxesAsync(workerProfileId, cancellation);

    /// <summary>
    /// Removes a serverless activity declaration for a worker profile.
    /// </summary>
    /// <param name="workerProfileId">The worker profile ID whose declaration should be removed.</param>
    /// <param name="cancellation">The cancellation token used to cancel the request.</param>
    /// <returns>A task that completes when DTS removes the declaration.</returns>
    public Task RemoveServerlessActivityDeclarationAsync(
        string workerProfileId,
        CancellationToken cancellation = default)
        => this.client.RemoveServerlessActivityDeclarationAsync(workerProfileId, cancellation);

    /// <summary>
    /// Streams logs from a serverless activity sandbox.
    /// </summary>
    /// <param name="dtsSandboxIdentifier">The DTS sandbox identifier to stream logs from.</param>
    /// <param name="tail">The number of historical log lines to include before streaming live logs.</param>
    /// <param name="cancellation">The cancellation token used to stop streaming.</param>
    /// <returns>An async stream of sandbox log lines.</returns>
    public IAsyncEnumerable<ServerlessSandboxLogLine> StreamSandboxLogsAsync(
        string dtsSandboxIdentifier,
        int tail = 100,
        CancellationToken cancellation = default)
        => this.client.StreamSandboxLogsAsync(dtsSandboxIdentifier, tail, cancellation);
}
