// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
    /// Removes a serverless activity declaration for a worker profile.
    /// </summary>
    /// <param name="workerProfileId">The worker profile ID whose declaration should be removed.</param>
    /// <param name="cancellation">The cancellation token used to cancel the request.</param>
    /// <returns>A task that completes when DTS removes the declaration.</returns>
    public Task RemoveServerlessActivityDeclarationAsync(
        string workerProfileId,
        CancellationToken cancellation = default)
        => this.client.RemoveServerlessActivityDeclarationAsync(workerProfileId, cancellation);
}
