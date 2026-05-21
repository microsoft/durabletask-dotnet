// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Proto = Microsoft.DurableTask.Protobuf.Serverless;

namespace Microsoft.DurableTask.Client.AzureManaged;

/// <summary>
/// Extension methods for the generated serverless activities gRPC client.
/// </summary>
public static class ServerlessActivitiesClientExtensions
{
    /// <summary>
    /// Removes a serverless activity declaration for a worker profile using task hub metadata already configured on the gRPC channel.
    /// </summary>
    /// <param name="client">The generated serverless activities gRPC client.</param>
    /// <param name="workerProfileId">The worker profile ID whose declaration should be removed.</param>
    /// <param name="cancellation">The cancellation token used to cancel the request.</param>
    /// <returns>A task that completes when DTS removes the declaration.</returns>
    public static Task RemoveServerlessActivityDeclarationAsync(
        this Proto.ServerlessActivities.ServerlessActivitiesClient client,
        string workerProfileId,
        CancellationToken cancellation = default)
    {
        return RemoveServerlessActivityDeclarationCoreAsync(
            client,
            workerProfileId,
            cancellation);
    }

    static async Task RemoveServerlessActivityDeclarationCoreAsync(
        Proto.ServerlessActivities.ServerlessActivitiesClient client,
        string workerProfileId,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(client);
        ValidateRequired(workerProfileId, nameof(workerProfileId), "Worker profile ID is required.");

        Proto.RemoveServerlessActivityDeclarationRequest request = new()
        {
            WorkerProfileId = workerProfileId,
        };

        using AsyncUnaryCall<Proto.RemoveServerlessActivityDeclarationResult> call = client.RemoveServerlessActivityDeclarationAsync(
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
