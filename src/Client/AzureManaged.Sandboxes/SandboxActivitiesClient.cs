// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.AzureManaged.Internal;
using Proto = Microsoft.DurableTask.Protobuf.Sandboxes;

namespace Microsoft.DurableTask.Client.AzureManaged;

/// <summary>
/// Client for DTS on-demand sandbox activity management operations.
/// </summary>
public sealed class SandboxActivitiesClient
{
    readonly ISandboxActivitiesTransport transport;
    readonly SandboxWorkerProfileProvider workerProfileProvider;
    readonly string taskHub;

    /// <summary>
    /// Initializes a new instance of the <see cref="SandboxActivitiesClient"/> class.
    /// </summary>
    /// <param name="transport">The transport used to call DTS on-demand sandbox management operations.</param>
    /// <param name="taskHub">The task hub whose workerProfiles should be sent to DTS.</param>
    /// <param name="workerProfileProvider">The workerProfile provider.</param>
    internal SandboxActivitiesClient(
        ISandboxActivitiesTransport transport,
        string taskHub,
        SandboxWorkerProfileProvider workerProfileProvider)
    {
        this.transport = Check.NotNull(transport);
        this.workerProfileProvider = Check.NotNull(workerProfileProvider);
        this.taskHub = string.IsNullOrWhiteSpace(taskHub)
            ? throw new ArgumentException("Task hub name is required.", nameof(taskHub))
            : taskHub.Trim();
    }

    /// <summary>
    /// Enables on-demand sandbox activities declared by worker profiles for the configured task hub.
    /// </summary>
    /// <param name="cancellation">The cancellation token used to cancel the request.</param>
    /// <returns>A task that completes when DTS accepts all workerProfiles.</returns>
    public async Task EnableSandboxActivitiesAsync(CancellationToken cancellation = default)
    {
        IReadOnlyList<SandboxWorkerProfileOptions> workerProfiles = this.workerProfileProvider.ResolveWorkerProfiles(this.taskHub);
        foreach (SandboxWorkerProfileOptions options in workerProfiles)
        {
            SandboxActivityMetadata.Activity[] activities = SandboxWorkerProfileBuilder.ResolveActivities(options.Activities);
            if (activities.Length == 0)
            {
                continue;
            }

            Proto.SandboxWorkerProfile workerProfile =
                SandboxWorkerProfileBuilder.BuildWorkerProfile(options, activities);
            await this.transport.DeclareSandboxWorkerProfileAsync(
                    workerProfile,
                    this.taskHub,
                    cancellation)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Removes an on-demand sandbox activity workerProfile for a worker profile.
    /// </summary>
    /// <param name="workerProfileId">The worker profile ID whose workerProfile should be removed.</param>
    /// <param name="cancellation">The cancellation token used to cancel the request.</param>
    /// <returns>A task that completes when DTS removes the workerProfile.</returns>
    public Task RemoveSandboxWorkerProfileAsync(
        string workerProfileId,
        CancellationToken cancellation = default)
    {
        string normalizedWorkerProfileId = string.IsNullOrWhiteSpace(workerProfileId)
            ? throw new ArgumentException("Worker profile ID is required.", nameof(workerProfileId))
            : workerProfileId.Trim();

        return this.transport.RemoveSandboxWorkerProfileAsync(
            normalizedWorkerProfileId,
            this.taskHub,
            cancellation);
    }
}
