// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Proto = Microsoft.DurableTask.Protobuf.OnDemandSandbox;

namespace Microsoft.DurableTask.Client.AzureManaged;

/// <summary>
/// Extension methods for the generated on-demand sandbox activities gRPC client.
/// </summary>
public static class OnDemandSandboxActivitiesClientExtensions
{
    /// <summary>
    /// Removes an on-demand sandbox activity declaration for a worker profile using task hub metadata already configured on the gRPC channel.
    /// </summary>
    /// <param name="client">The generated on-demand sandbox activities gRPC client.</param>
    /// <param name="workerProfileId">The worker profile ID whose declaration should be removed.</param>
    /// <param name="cancellation">The cancellation token used to cancel the request.</param>
    /// <returns>A task that completes when DTS removes the declaration.</returns>
    public static Task RemoveOnDemandSandboxActivityDeclarationAsync(
        this Proto.OnDemandSandboxActivities.OnDemandSandboxActivitiesClient client,
        string workerProfileId,
        CancellationToken cancellation = default)
    {
        return RemoveOnDemandSandboxActivityDeclarationCoreAsync(
            client,
            workerProfileId,
            cancellation);
    }

    static async Task RemoveOnDemandSandboxActivityDeclarationCoreAsync(
        Proto.OnDemandSandboxActivities.OnDemandSandboxActivitiesClient client,
        string workerProfileId,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(client);
        ValidateRequired(workerProfileId, nameof(workerProfileId), "Worker profile ID is required.");

        Proto.RemoveOnDemandSandboxActivityDeclarationRequest request = new()
        {
            WorkerProfileId = workerProfileId,
        };

        using AsyncUnaryCall<Proto.RemoveOnDemandSandboxActivityDeclarationResult> call = client.RemoveOnDemandSandboxActivityDeclarationAsync(
            request,
            headers: null,
            cancellationToken: cancellation);
        await call.ResponseAsync.ConfigureAwait(false);
    }

    static void ValidateRequired(string value, string parameterName, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(message, parameterName);
        }
    }
}
