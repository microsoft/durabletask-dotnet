// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proto = Microsoft.DurableTask.Protobuf.OnDemandSandbox;

namespace Microsoft.DurableTask.Worker.AzureManaged.OnDemandSandbox;

/// <summary>
/// Hosted service that declares on-demand sandbox activities with DTS when the local worker starts.
/// </summary>
sealed class OnDemandSandboxActivityDeclarationHostedService : IHostedService
{
    readonly IOnDemandSandboxActivitiesClient client;
    readonly IReadOnlyList<OnDemandSandboxOptions> declarations;
    readonly OnDemandSandboxWorkerRuntimeOptions? runtimeOptions;
    readonly ILogger<OnDemandSandboxActivityDeclarationHostedService> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OnDemandSandboxActivityDeclarationHostedService"/> class.
    /// </summary>
    /// <param name="client">The on-demand sandbox activities client.</param>
    /// <param name="declarations">The on-demand sandbox declaration options.</param>
    /// <param name="runtimeOptions">The optional on-demand sandbox worker runtime options.</param>
    /// <param name="logger">The logger.</param>
    public OnDemandSandboxActivityDeclarationHostedService(
        IOnDemandSandboxActivitiesClient client,
        IReadOnlyList<OnDemandSandboxOptions> declarations,
        OnDemandSandboxWorkerRuntimeOptions? runtimeOptions,
        ILogger<OnDemandSandboxActivityDeclarationHostedService> logger)
    {
        this.client = Check.NotNull(client);
        this.declarations = Check.NotNull(declarations);
        this.runtimeOptions = runtimeOptions;
        this.logger = Check.NotNull(logger);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OnDemandSandboxActivityDeclarationHostedService"/> class.
    /// </summary>
    /// <param name="client">The on-demand sandbox activities client.</param>
    /// <param name="declaration">The on-demand sandbox declaration options.</param>
    /// <param name="runtimeOptions">The optional on-demand sandbox worker runtime options.</param>
    /// <param name="logger">The logger.</param>
    public OnDemandSandboxActivityDeclarationHostedService(
        IOnDemandSandboxActivitiesClient client,
        OnDemandSandboxOptions declaration,
        OnDemandSandboxWorkerRuntimeOptions? runtimeOptions,
        ILogger<OnDemandSandboxActivityDeclarationHostedService> logger)
        : this(client, [declaration], runtimeOptions, logger)
    {
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (this.runtimeOptions?.Mode == OnDemandSandboxMode.OnDemandSandboxInclude)
        {
            return;
        }

        if (this.declarations.Count == 0)
        {
            Logs.NoOnDemandSandboxActivitiesForDeclaration(this.logger, string.Empty);
            return;
        }

        foreach (OnDemandSandboxOptions options in this.declarations)
        {
            string[] activityNames = OnDemandSandboxActivityConfiguration.ResolveActivityNames(options.ActivityNames);
            if (activityNames.Length == 0)
            {
                Logs.NoOnDemandSandboxActivitiesForDeclaration(this.logger, options.TaskHub);
                continue;
            }

            Proto.OnDemandSandboxActivityDeclaration declaration = OnDemandSandboxActivityConfiguration.BuildDeclaration(
                options,
                activityNames);
            try
            {
                await this.client.DeclareOnDemandSandboxActivitiesAsync(
                    declaration,
                    options.TaskHub,
                    cancellationToken).ConfigureAwait(false);
                Logs.OnDemandSandboxActivitiesDeclared(
                    this.logger,
                    options.TaskHub,
                    declaration.WorkerProfileId,
                    declaration.ActivityNames.Count,
                    declaration.Image?.ImageRef ?? string.Empty);
            }
            catch (Exception ex)
            {
                Logs.OnDemandSandboxActivityDeclarationFailed(this.logger, ex, options.TaskHub);
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
