// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proto = Microsoft.DurableTask.Protobuf.Serverless;

namespace Microsoft.DurableTask.Worker.AzureManaged.Serverless;

/// <summary>
/// Hosted service that declares remote activities with DTS when the local worker starts.
/// </summary>
sealed partial class RemoteActivityDeclarationHostedService : IHostedService
{
    readonly IServerlessActivitiesClient client;
    readonly RemoteActivityOptions options;
    readonly ILogger<RemoteActivityDeclarationHostedService> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoteActivityDeclarationHostedService"/> class.
    /// </summary>
    /// <param name="client">The serverless activities client.</param>
    /// <param name="options">The remote activity options.</param>
    /// <param name="logger">The logger.</param>
    public RemoteActivityDeclarationHostedService(
        IServerlessActivitiesClient client,
        RemoteActivityOptions options,
        ILogger<RemoteActivityDeclarationHostedService> logger)
    {
        this.client = Check.NotNull(client);
        this.options = Check.NotNull(options);
        this.logger = Check.NotNull(logger);
    }

    /// <summary>
    /// Gets a task completed when the declaration attempt succeeds, is skipped, or fails.
    /// </summary>
    internal TaskCompletionSource<Proto.RemoteActivityDeclarationResult?> Ready { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        string[] activityNames = RemoteActivityConfiguration.ResolveActivityNames(this.options.ActivityNames);
        if (activityNames.Length == 0)
        {
            Log.NoRemoteActivitiesDiscovered(this.logger, this.options.TaskHub);
            this.Ready.TrySetResult(null);
            return;
        }

        Proto.RemoteActivityDeclaration declaration = RemoteActivityConfiguration.BuildDeclaration(this.options, activityNames);
        int maxAttempts = Math.Max(1, this.options.DeclarationRetryMaxAttempts);
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                Proto.RemoteActivityDeclarationResult result = await this.client.DeclareRemoteActivitiesAsync(
                    declaration,
                    cancellationToken).ConfigureAwait(false);
                this.Ready.TrySetResult(result);
                Log.RemoteActivitiesDeclared(
                    this.logger,
                    declaration.TaskHub,
                    declaration.WorkerProfileId,
                    declaration.ActivityNames.Count,
                    declaration.Image?.ImageRef ?? string.Empty);
                return;
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                Log.RemoteActivityDeclarationRetry(this.logger, ex, declaration.TaskHub, attempt, maxAttempts);
                if (this.options.DeclarationRetryDelay > TimeSpan.Zero)
                {
                    await Task.Delay(this.options.DeclarationRetryDelay, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                this.Ready.TrySetException(ex);
                Log.RemoteActivityDeclarationFailed(this.logger, ex, declaration.TaskHub);
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
        /// Logs that no remote activities were discovered for declaration.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="hub">The task hub name.</param>
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Information,
            Message = "No remote activities discovered for hub={Hub}; skipping declaration")]
        public static partial void NoRemoteActivitiesDiscovered(ILogger logger, string hub);

        /// <summary>
        /// Logs a successful remote activity declaration.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="hub">The task hub name.</param>
        /// <param name="workerProfile">The worker profile ID.</param>
        /// <param name="count">The declared activity count.</param>
        /// <param name="image">The remote worker image reference.</param>
        [LoggerMessage(
            EventId = 2,
            Level = LogLevel.Information,
            Message = "Remote activities declared hub={Hub} workerProfile={WorkerProfile} count={Count} image={Image}")]
        public static partial void RemoteActivitiesDeclared(
            ILogger logger,
            string hub,
            string workerProfile,
            int count,
            string image);

        /// <summary>
        /// Logs a transient remote activity declaration failure that will be retried.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The transient exception.</param>
        /// <param name="hub">The task hub name.</param>
        /// <param name="attempt">The current attempt number.</param>
        /// <param name="maxAttempts">The maximum attempt count.</param>
        [LoggerMessage(
            EventId = 3,
            Level = LogLevel.Warning,
            Message = "Remote activity declaration failed transiently hub={Hub} attempt={Attempt} maxAttempts={MaxAttempts}")]
        public static partial void RemoteActivityDeclarationRetry(
            ILogger logger,
            Exception exception,
            string hub,
            int attempt,
            int maxAttempts);

        /// <summary>
        /// Logs a failed remote activity declaration.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The declaration exception.</param>
        /// <param name="hub">The task hub name.</param>
        [LoggerMessage(
            EventId = 4,
            Level = LogLevel.Error,
            Message = "Remote activity declaration failed hub={Hub}")]
        public static partial void RemoteActivityDeclarationFailed(ILogger logger, Exception exception, string hub);
    }
}
