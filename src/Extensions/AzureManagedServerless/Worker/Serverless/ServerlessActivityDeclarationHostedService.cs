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
sealed partial class ServerlessActivityDeclarationHostedService : IHostedService
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
            Log.NoServerlessActivitiesDiscovered(this.logger, this.options.TaskHub);
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
                Log.ServerlessActivitiesDeclared(
                    this.logger,
                    this.options.TaskHub,
                    declaration.WorkerProfileId,
                    declaration.ActivityNames.Count,
                    declaration.Image?.ImageRef ?? string.Empty);
                return;
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                Log.ServerlessActivityDeclarationRetry(this.logger, ex, this.options.TaskHub, attempt, maxAttempts);
                if (this.options.DeclarationRetryDelay > TimeSpan.Zero)
                {
                    await Task.Delay(this.options.DeclarationRetryDelay, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                this.Ready.TrySetException(ex);
                Log.ServerlessActivityDeclarationFailed(this.logger, ex, this.options.TaskHub);
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

    static partial class Log
    {
        /// <summary>
        /// Logs that no serverless activities were discovered for declaration.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="hub">The task hub name.</param>
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Information,
            Message = "No serverless activities discovered for hub={Hub}; skipping declaration")]
        public static partial void NoServerlessActivitiesDiscovered(ILogger logger, string hub);

        /// <summary>
        /// Logs a successful serverless activity declaration.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="hub">The task hub name.</param>
        /// <param name="workerProfile">The worker profile ID.</param>
        /// <param name="count">The declared activity count.</param>
        /// <param name="image">The serverless worker image reference.</param>
        [LoggerMessage(
            EventId = 2,
            Level = LogLevel.Information,
            Message = "Serverless activities declared hub={Hub} workerProfile={WorkerProfile} count={Count} image={Image}")]
        public static partial void ServerlessActivitiesDeclared(
            ILogger logger,
            string hub,
            string workerProfile,
            int count,
            string image);

        /// <summary>
        /// Logs a transient serverless activity declaration failure that will be retried.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The transient exception.</param>
        /// <param name="hub">The task hub name.</param>
        /// <param name="attempt">The completed attempt count.</param>
        /// <param name="maxAttempts">The maximum attempt count.</param>
        [LoggerMessage(
            EventId = 3,
            Level = LogLevel.Warning,
            Message = "Serverless activity declaration failed transiently hub={Hub} attempt={Attempt} maxAttempts={MaxAttempts}")]
        public static partial void ServerlessActivityDeclarationRetry(
            ILogger logger,
            Exception exception,
            string hub,
            int attempt,
            int maxAttempts);

        /// <summary>
        /// Logs a failed serverless activity declaration.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The declaration exception.</param>
        /// <param name="hub">The task hub name.</param>
        [LoggerMessage(
            EventId = 4,
            Level = LogLevel.Error,
            Message = "Serverless activity declaration failed hub={Hub}")]
        public static partial void ServerlessActivityDeclarationFailed(ILogger logger, Exception exception, string hub);
    }
}
