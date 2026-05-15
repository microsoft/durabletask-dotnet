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
    readonly ServerlessOptions options;
    readonly ILogger<ServerlessActivityDeclarationHostedService> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerlessActivityDeclarationHostedService"/> class.
    /// </summary>
    /// <param name="client">The serverless activities client.</param>
    /// <param name="options">The serverless options.</param>
    /// <param name="logger">The logger.</param>
    public ServerlessActivityDeclarationHostedService(
        IServerlessActivitiesClient client,
        ServerlessOptions options,
        ILogger<ServerlessActivityDeclarationHostedService> logger)
    {
        this.client = Check.NotNull(client);
        this.options = Check.NotNull(options);
        this.logger = Check.NotNull(logger);
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (this.options.Mode == ServerlessMode.ServerlessInclude)
        {
            return;
        }

        string[] activityNames = ServerlessActivityConfiguration.ResolveActivityNames(this.options.ActivityNames);
        if (activityNames.Length == 0)
        {
            Logs.NoServerlessActivitiesForDeclaration(this.logger, this.options.TaskHub);
            return;
        }

        Proto.ServerlessActivityDeclaration declaration = ServerlessActivityConfiguration.BuildDeclaration(
            this.options,
            activityNames);
        try
        {
            await this.client.DeclareServerlessActivitiesAsync(
                declaration,
                this.options.TaskHub,
                cancellationToken).ConfigureAwait(false);
            Logs.ServerlessActivitiesDeclared(
                this.logger,
                this.options.TaskHub,
                declaration.WorkerProfileId,
                declaration.ActivityNames.Count,
                declaration.Image?.ImageRef ?? string.Empty);
        }
        catch (Exception ex)
        {
            Logs.ServerlessActivityDeclarationFailed(this.logger, ex, this.options.TaskHub);
            throw;
        }
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
