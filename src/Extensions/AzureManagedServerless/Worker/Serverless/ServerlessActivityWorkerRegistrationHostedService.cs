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
sealed class ServerlessActivityWorkerRegistrationHostedService : IHostedService, IAsyncDisposable
{
    readonly IServerlessActivitiesClient client;
    readonly ServerlessOptions options;
    readonly ILogger<ServerlessActivityWorkerRegistrationHostedService> logger;
    readonly IHostApplicationLifetime? lifetime;
    readonly ServerlessActivityTracker? activityTracker;
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
    /// <param name="activityTracker">The optional activity tracker used to report live in-flight activity count.</param>
    public ServerlessActivityWorkerRegistrationHostedService(
        IServerlessActivitiesClient client,
        ServerlessOptions options,
        ILogger<ServerlessActivityWorkerRegistrationHostedService> logger,
        IHostApplicationLifetime? lifetime = null,
        ServerlessActivityTracker? activityTracker = null)
    {
        this.client = Check.NotNull(client);
        this.options = Check.NotNull(options);
        this.logger = Check.NotNull(logger);
        this.lifetime = lifetime;
        this.activityTracker = activityTracker;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (this.options.Mode != ServerlessMode.ServerlessInclude)
        {
            this.pump = Task.CompletedTask;
            return;
        }

        string[] activityNames = ServerlessActivityConfiguration.ResolveActivityNames(this.options.ActivityNames);
        if (activityNames.Length == 0)
        {
            Logs.NoServerlessActivitiesForWorkerRegistration(this.logger, this.options.TaskHub);
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
            Logs.ServerlessActivityWorkerRegistered(
                this.logger,
                startMessage.Start.TaskHub,
                startMessage.Start.WorkerInstanceId,
                activityNames.Length,
                startMessage.Start.Substrate,
                startMessage.Start.SandboxId);
        }
        catch (Exception ex)
        {
            Logs.ServerlessActivityWorkerRegistrationFailed(this.logger, ex, this.options.TaskHub);
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
                int activeActivitiesCount = this.activityTracker?.InFlightCount ?? 0;
                await registrationSession.WriteMessageAsync(
                    ServerlessActivityConfiguration.BuildWorkerHeartbeat(activeActivitiesCount)).ConfigureAwait(false);
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
        Logs.ServerlessActivityWorkerRegistrationFailed(this.logger, exception, this.options.TaskHub);
        this.lifetime?.StopApplication();
    }
}
