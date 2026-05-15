// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
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

    /// <summary>
    /// Gets a task completed when the declaration attempt succeeds, is skipped, or fails.
    /// </summary>
    internal TaskCompletionSource<Proto.ServerlessActivityDeclarationResult?> Ready { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (this.options.Mode == ServerlessMode.ServerlessInclude)
        {
            this.Ready.TrySetResult(null);
            return;
        }

        string[] activityNames = ServerlessActivityConfiguration.ResolveActivityNames(this.options.ActivityNames);
        if (activityNames.Length == 0)
        {
            Logs.NoServerlessActivitiesForDeclaration(this.logger, this.options.TaskHub);
            this.Ready.TrySetResult(null);
            return;
        }

        Proto.ServerlessActivityDeclaration declaration = ServerlessActivityConfiguration.BuildDeclaration(this.options, activityNames);
        int maxAttempts = Math.Max(1, this.options.DeclarationRetryMaxAttempts);
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                Proto.ServerlessActivityDeclarationResult result = await this.client.DeclareServerlessActivitiesAsync(
                    declaration,
                    this.options.TaskHub,
                    cancellationToken).ConfigureAwait(false);
                this.Ready.TrySetResult(result);
                Logs.ServerlessActivitiesDeclared(
                    this.logger,
                    this.options.TaskHub,
                    declaration.WorkerProfileId,
                    declaration.ActivityNames.Count,
                    declaration.Image?.ImageRef ?? string.Empty);
                return;
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                Logs.ServerlessActivityDeclarationRetry(this.logger, ex, this.options.TaskHub, attempt, maxAttempts);
                if (this.options.DeclarationRetryDelay > TimeSpan.Zero)
                {
                    await Task.Delay(this.options.DeclarationRetryDelay, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                this.Ready.TrySetException(ex);
                Logs.ServerlessActivityDeclarationFailed(this.logger, ex, this.options.TaskHub);
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    static bool IsTransient(Exception exception) =>
        exception is RpcException rpcException
        && (rpcException.StatusCode == StatusCode.Unavailable
            || rpcException.StatusCode == StatusCode.DeadlineExceeded
            || rpcException.StatusCode == StatusCode.ResourceExhausted
            || rpcException.StatusCode == StatusCode.Internal);
}
