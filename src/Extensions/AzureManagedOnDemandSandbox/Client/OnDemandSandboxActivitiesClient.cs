// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Proto = Microsoft.DurableTask.Protobuf.OnDemandSandbox;

namespace Microsoft.DurableTask.Client.AzureManaged;

/// <summary>
/// Client for DTS on-demand sandbox activity management operations.
/// </summary>
public sealed class OnDemandSandboxActivitiesClient
{
    readonly Proto.OnDemandSandboxActivities.OnDemandSandboxActivitiesClient client;

    /// <summary>
    /// Initializes a new instance of the <see cref="OnDemandSandboxActivitiesClient"/> class.
    /// </summary>
    /// <param name="client">The generated gRPC client used to call DTS on-demand sandbox management operations.</param>
    internal OnDemandSandboxActivitiesClient(Proto.OnDemandSandboxActivities.OnDemandSandboxActivitiesClient client)
    {
        this.client = client;
    }

    /// <summary>
    /// Removes an on-demand sandbox activity declaration for a worker profile.
    /// </summary>
    /// <param name="workerProfileId">The worker profile ID whose declaration should be removed.</param>
    /// <param name="cancellation">The cancellation token used to cancel the request.</param>
    /// <returns>A task that completes when DTS removes the declaration.</returns>
    public Task RemoveOnDemandSandboxActivityDeclarationAsync(
        string workerProfileId,
        CancellationToken cancellation = default)
        => this.client.RemoveOnDemandSandboxActivityDeclarationAsync(workerProfileId, cancellation);
}
