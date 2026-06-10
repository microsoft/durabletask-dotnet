// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Microsoft.DurableTask.Worker.AzureManaged.OnDemandSandbox;
using Proto = Microsoft.DurableTask.Protobuf.OnDemandSandbox;

namespace Microsoft.DurableTask.Client.AzureManaged;

/// <summary>
/// Client for DTS on-demand sandbox activity management operations.
/// </summary>
public sealed class OnDemandSandboxActivitiesClient
{
    readonly Proto.OnDemandSandboxActivities.OnDemandSandboxActivitiesClient client;
    readonly string taskHub;
    readonly bool attachTaskHubMetadata;

    /// <summary>
    /// Initializes a new instance of the <see cref="OnDemandSandboxActivitiesClient"/> class.
    /// </summary>
    /// <param name="client">The generated gRPC client used to call DTS on-demand sandbox management operations.</param>
    /// <param name="taskHub">The task hub whose declarations should be sent to DTS.</param>
    /// <param name="attachTaskHubMetadata">True to add per-call task hub metadata when the underlying channel does not already do so.</param>
    internal OnDemandSandboxActivitiesClient(
        Proto.OnDemandSandboxActivities.OnDemandSandboxActivitiesClient client,
        string taskHub,
        bool attachTaskHubMetadata = true)
    {
        this.client = client;
        this.taskHub = string.IsNullOrWhiteSpace(taskHub)
            ? throw new ArgumentException("Task hub name is required.", nameof(taskHub))
            : taskHub.Trim();
        this.attachTaskHubMetadata = attachTaskHubMetadata;
    }

    /// <summary>
    /// Enables on-demand sandbox activities declared by worker profiles for the configured task hub.
    /// </summary>
    /// <param name="cancellation">The cancellation token used to cancel the request.</param>
    /// <returns>A task that completes when DTS accepts all declarations.</returns>
    public async Task EnableOnDemandSandboxActivitiesAsync(CancellationToken cancellation = default)
    {
        IReadOnlyList<OnDemandSandboxOptions> declarations =
            OnDemandSandboxActivityDeclarationResolver.ResolveDeclarations(this.taskHub);
        foreach (OnDemandSandboxOptions options in declarations)
        {
            string[] activityNames = OnDemandSandboxActivityDeclarationBuilder.ResolveActivityNames(options.ActivityNames);
            if (activityNames.Length == 0)
            {
                continue;
            }

            Proto.OnDemandSandboxActivityDeclaration declaration =
                OnDemandSandboxActivityDeclarationBuilder.BuildDeclaration(options, activityNames);
            using AsyncUnaryCall<Proto.OnDemandSandboxActivityDeclarationResult> call =
                this.client.DeclareOnDemandSandboxActivitiesAsync(
                    declaration,
                    headers: this.CreateTaskHubHeaders(),
                    cancellationToken: cancellation);
            await call.ResponseAsync.ConfigureAwait(false);
        }
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
    {
        string normalizedWorkerProfileId = string.IsNullOrWhiteSpace(workerProfileId)
            ? throw new ArgumentException("Worker profile ID is required.", nameof(workerProfileId))
            : workerProfileId.Trim();

        Proto.RemoveOnDemandSandboxActivityDeclarationRequest request = new()
        {
            WorkerProfileId = normalizedWorkerProfileId,
        };

        return this.RemoveOnDemandSandboxActivityDeclarationCoreAsync(request, cancellation);
    }

    async Task RemoveOnDemandSandboxActivityDeclarationCoreAsync(
        Proto.RemoveOnDemandSandboxActivityDeclarationRequest request,
        CancellationToken cancellation)
    {
        using AsyncUnaryCall<Proto.RemoveOnDemandSandboxActivityDeclarationResult> call =
            this.client.RemoveOnDemandSandboxActivityDeclarationAsync(
                request,
                headers: this.CreateTaskHubHeaders(),
                cancellationToken: cancellation);
        await call.ResponseAsync.ConfigureAwait(false);
    }

    Metadata? CreateTaskHubHeaders() => this.attachTaskHubMetadata
        ? new Metadata { { "taskhub", this.taskHub }, }
        : null;
}
