// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proto = Microsoft.DurableTask.Protobuf.Serverless;

namespace Microsoft.DurableTask.Worker.AzureManaged.Serverless;

/// <summary>
/// Hosted service that registers a running process as a serverless activity worker with DTS.
/// </summary>
sealed partial class ServerlessActivityWorkerRegistrationHostedService : IHostedService, IAsyncDisposable
{
    readonly IServerlessActivitiesClient client;
    readonly ServerlessOptions options;
    readonly ILogger<ServerlessActivityWorkerRegistrationHostedService> logger;
    readonly IHostApplicationLifetime? lifetime;
    CancellationTokenSource? cts;
    IServerlessActivityWorkerSession? session;
    Task? pump;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerlessActivityWorkerRegistrationHostedService"/> class.
    /// </summary>
    /// <param name="client">The serverless activities client.</param>
    /// <param name="options">The serverless options.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="lifetime">The optional application lifetime used to stop the host when the registration stream fails.</param>
    public ServerlessActivityWorkerRegistrationHostedService(
        IServerlessActivitiesClient client,
        ServerlessOptions options,
        ILogger<ServerlessActivityWorkerRegistrationHostedService> logger,
        IHostApplicationLifetime? lifetime = null)
    {
        this.client = Check.NotNull(client);
        this.options = Check.NotNull(options);
        this.logger = Check.NotNull(logger);
        this.lifetime = lifetime;
    }

    /// <summary>
    /// Gets a task completed when the worker registration succeeds, is skipped, or fails.
    /// </summary>
    internal TaskCompletionSource<bool> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (this.options.Mode != ServerlessMode.ServerlessInclude)
        {
            this.Ready.TrySetResult(true);
            this.pump = Task.CompletedTask;
            return;
        }

        string[] activityNames = ServerlessActivityConfiguration.ResolveActivityNames(this.options.ActivityNames);
        if (activityNames.Length == 0)
        {
            Log.NoServerlessActivitiesDiscovered(this.logger, this.options.TaskHub);
            this.Ready.TrySetResult(true);
            this.pump = Task.CompletedTask;
            return;
        }

        CancellationTokenSource registrationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        this.cts = registrationCts;
        IServerlessActivityWorkerSession registrationSession = this.client.OpenServerlessActivityWorkerSession(this.options.TaskHub, registrationCts.Token);
        this.session = registrationSession;

        Proto.ServerlessActivityWorkerMessage startMessage = ServerlessActivityConfiguration.BuildWorkerStart(this.options);
        try
        {
            await registrationSession.WriteMessageAsync(startMessage).ConfigureAwait(false);
            this.Ready.TrySetResult(true);
            Log.ServerlessActivityWorkerRegistered(
                this.logger,
                startMessage.Start.TaskHub,
                startMessage.Start.WorkerInstanceId,
                activityNames.Length,
                startMessage.Start.Substrate,
                startMessage.Start.SandboxId);
        }
        catch (Exception ex)
        {
            this.Ready.TrySetException(ex);
            Log.ServerlessActivityWorkerRegistrationFailed(this.logger, ex, this.options.TaskHub);
            throw;
        }

        this.pump = Task.Run(
            () => this.PumpHeartbeatsAsync(registrationSession, registrationCts.Token),
            CancellationToken.None);
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? localCts = this.cts;
        IServerlessActivityWorkerSession? localSession = this.session;
        localCts?.Cancel();

        if (localSession is not null)
        {
            try
            {
                await localSession.CompleteAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or RpcException)
            {
            }
        }

        if (this.pump is not null)
        {
            try
            {
                await this.pump.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or RpcException)
            {
            }
        }

        if (localSession is not null)
        {
            await localSession.DisposeAsync().ConfigureAwait(false);
        }

        localCts?.Dispose();
        if (ReferenceEquals(this.cts, localCts))
        {
            this.cts = null;
        }

        if (ReferenceEquals(this.session, localSession))
        {
            this.session = null;
        }

        this.pump = Task.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => new(this.StopAsync(CancellationToken.None));

    async Task PumpHeartbeatsAsync(
        IServerlessActivityWorkerSession registrationSession,
        CancellationToken cancellationToken)
    {
        try
        {
            using PeriodicTimer timer = new(this.options.HeartbeatInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await registrationSession.WriteMessageAsync(
                    ServerlessActivityConfiguration.BuildWorkerHeartbeat(activeActivitiesCount: 0)).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            this.HandleRegistrationStreamFailure(ex);
        }
    }

    void HandleRegistrationStreamFailure(Exception exception)
    {
        Log.ServerlessActivityWorkerRegistrationFailed(this.logger, exception, this.options.TaskHub);
        this.lifetime?.StopApplication();
    }

    static partial class Log
    {
        /// <summary>
        /// Logs that no serverless activities were discovered for live worker registration.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="hub">The task hub name.</param>
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Information,
            Message = "No serverless activities discovered for worker hub={Hub}; skipping live registration")]
        public static partial void NoServerlessActivitiesDiscovered(ILogger logger, string hub);

        /// <summary>
        /// Logs a successful serverless activity worker registration.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="hub">The task hub name.</param>
        /// <param name="worker">The worker instance ID.</param>
        /// <param name="count">The activity count.</param>
        /// <param name="substrate">The substrate kind.</param>
        /// <param name="sandboxId">The sandbox ID.</param>
        [LoggerMessage(
            EventId = 2,
            Level = LogLevel.Information,
            Message = "Serverless activity worker registered hub={Hub} worker={Worker} count={Count} substrate={Substrate} sandboxId={SandboxId}")]
        public static partial void ServerlessActivityWorkerRegistered(
            ILogger logger,
            string hub,
            string worker,
            int count,
            Proto.SubstrateKind substrate,
            string sandboxId);

        /// <summary>
        /// Logs a failed serverless activity worker registration stream.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The registration exception.</param>
        /// <param name="hub">The task hub name.</param>
        [LoggerMessage(
            EventId = 3,
            Level = LogLevel.Error,
            Message = "Serverless activity worker registration stream failed hub={Hub}")]
        public static partial void ServerlessActivityWorkerRegistrationFailed(ILogger logger, Exception exception, string hub);
    }
}
