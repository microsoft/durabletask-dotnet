// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proto = Microsoft.DurableTask.Protobuf.Serverless;

namespace Microsoft.DurableTask.Worker.AzureManaged.Serverless;

/// <summary>
/// Hosted service that declares serverless activities with DTS when the local worker starts.
/// </summary>
sealed class ServerlessActivityDeclarationHostedService : IHostedService
{
    readonly IServerlessActivitiesClient client;
    readonly IReadOnlyList<ServerlessOptions> declarations;
    readonly ServerlessWorkerRuntimeOptions? runtimeOptions;
    readonly ILogger<ServerlessActivityDeclarationHostedService> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerlessActivityDeclarationHostedService"/> class.
    /// </summary>
    /// <param name="client">The serverless activities client.</param>
    /// <param name="declarations">The serverless declaration options.</param>
    /// <param name="runtimeOptions">The optional serverless worker runtime options.</param>
    /// <param name="logger">The logger.</param>
    public ServerlessActivityDeclarationHostedService(
        IServerlessActivitiesClient client,
        IReadOnlyList<ServerlessOptions> declarations,
        ServerlessWorkerRuntimeOptions? runtimeOptions,
        ILogger<ServerlessActivityDeclarationHostedService> logger)
    {
        this.client = Check.NotNull(client);
        this.declarations = Check.NotNull(declarations);
        this.runtimeOptions = runtimeOptions;
        this.logger = Check.NotNull(logger);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerlessActivityDeclarationHostedService"/> class.
    /// </summary>
    /// <param name="client">The serverless activities client.</param>
    /// <param name="declaration">The serverless declaration options.</param>
    /// <param name="runtimeOptions">The optional serverless worker runtime options.</param>
    /// <param name="logger">The logger.</param>
    public ServerlessActivityDeclarationHostedService(
        IServerlessActivitiesClient client,
        ServerlessOptions declaration,
        ServerlessWorkerRuntimeOptions? runtimeOptions,
        ILogger<ServerlessActivityDeclarationHostedService> logger)
        : this(client, [declaration], runtimeOptions, logger)
    {
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (this.runtimeOptions?.Mode == ServerlessMode.ServerlessInclude)
        {
            return;
        }

        if (this.declarations.Count == 0)
        {
            Logs.NoServerlessActivitiesForDeclaration(this.logger, string.Empty);
            return;
        }

        foreach (ServerlessOptions options in this.declarations)
        {
            string[] activityNames = ServerlessActivityConfiguration.ResolveActivityNames(options.ActivityNames);
            if (activityNames.Length == 0)
            {
                Logs.NoServerlessActivitiesForDeclaration(this.logger, options.TaskHub);
                continue;
            }

            Proto.ServerlessActivityDeclaration declaration = ServerlessActivityConfiguration.BuildDeclaration(
                options,
                activityNames);
            try
            {
                await this.client.DeclareServerlessActivitiesAsync(
                    declaration,
                    options.TaskHub,
                    cancellationToken).ConfigureAwait(false);
                Logs.ServerlessActivitiesDeclared(
                    this.logger,
                    options.TaskHub,
                    declaration.WorkerProfileId,
                    declaration.ActivityNames.Count,
                    declaration.Image?.ImageRef ?? string.Empty);
            }
            catch (Exception ex)
            {
                Logs.ServerlessActivityDeclarationFailed(this.logger, ex, options.TaskHub);
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
