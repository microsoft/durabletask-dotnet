// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.AzureManaged.Internal;
using Proto = Microsoft.DurableTask.Protobuf.OnDemandSandbox;

namespace Microsoft.DurableTask.Client.AzureManaged;

/// <summary>
/// Client for DTS on-demand sandbox activity management operations.
/// </summary>
public sealed class SandboxActivitiesClient
{
    readonly ISandboxActivitiesTransport transport;
    readonly SandboxActivityDeclarationProvider declarationProvider;
    readonly string taskHub;

    /// <summary>
    /// Initializes a new instance of the <see cref="SandboxActivitiesClient"/> class.
    /// </summary>
    /// <param name="transport">The transport used to call DTS on-demand sandbox management operations.</param>
    /// <param name="taskHub">The task hub whose declarations should be sent to DTS.</param>
    /// <param name="declarationProvider">The declaration provider.</param>
    internal SandboxActivitiesClient(
        ISandboxActivitiesTransport transport,
        string taskHub,
        SandboxActivityDeclarationProvider declarationProvider)
    {
        this.transport = Check.NotNull(transport);
        this.declarationProvider = Check.NotNull(declarationProvider);
        this.taskHub = string.IsNullOrWhiteSpace(taskHub)
            ? throw new ArgumentException("Task hub name is required.", nameof(taskHub))
            : taskHub.Trim();
    }

    /// <summary>
    /// Enables on-demand sandbox activities declared by worker profiles for the configured task hub.
    /// </summary>
    /// <param name="cancellation">The cancellation token used to cancel the request.</param>
    /// <returns>A task that completes when DTS accepts all declarations.</returns>
    public async Task EnableSandboxActivitiesAsync(CancellationToken cancellation = default)
    {
        IReadOnlyList<SandboxWorkerProfileOptions> declarations = this.declarationProvider.ResolveDeclarations(this.taskHub);
        foreach (SandboxWorkerProfileOptions options in declarations)
        {
            string[] activityNames = SandboxActivityDeclarationBuilder.ResolveActivityNames(options.ActivityNames);
            if (activityNames.Length == 0)
            {
                continue;
            }

            Proto.OnDemandSandboxActivityDeclaration declaration =
                SandboxActivityDeclarationBuilder.BuildDeclaration(options, activityNames);
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
    public Task RemoveSandboxActivityDeclarationAsync(
        string workerProfileId,
        CancellationToken cancellation = default)
    {
        string normalizedWorkerProfileId = string.IsNullOrWhiteSpace(workerProfileId)
            ? throw new ArgumentException("Worker profile ID is required.", nameof(workerProfileId))
            : workerProfileId.Trim();

        return this.transport.RemoveSandboxActivityDeclarationAsync(
            normalizedWorkerProfileId,
            this.taskHub,
            cancellation);
    }
}
