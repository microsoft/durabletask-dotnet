// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.AzureManaged.OnDemandSandbox;
using Proto = Microsoft.DurableTask.Protobuf.OnDemandSandbox;

namespace Microsoft.DurableTask.Client.AzureManaged;

/// <summary>
/// Client for DTS on-demand sandbox activity management operations.
/// </summary>
public sealed class OnDemandSandboxActivitiesClient
{
    readonly IOnDemandSandboxActivitiesTransport transport;
    readonly string taskHub;

    /// <summary>
    /// Initializes a new instance of the <see cref="OnDemandSandboxActivitiesClient"/> class.
    /// </summary>
    /// <param name="transport">The transport used to call DTS on-demand sandbox management operations.</param>
    /// <param name="taskHub">The task hub whose declarations should be sent to DTS.</param>
    internal OnDemandSandboxActivitiesClient(
        IOnDemandSandboxActivitiesTransport transport,
        string taskHub)
    {
        this.transport = Check.NotNull(transport);
        this.taskHub = string.IsNullOrWhiteSpace(taskHub)
            ? throw new ArgumentException("Task hub name is required.", nameof(taskHub))
            : taskHub.Trim();
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
            await this.transport.DeclareOnDemandSandboxActivitiesAsync(
                    declaration,
                    this.taskHub,
                    cancellation)
                .ConfigureAwait(false);
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

        return this.transport.RemoveOnDemandSandboxActivityDeclarationAsync(
            normalizedWorkerProfileId,
            this.taskHub,
            cancellation);
    }
}
