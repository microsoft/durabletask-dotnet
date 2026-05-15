// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
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
    readonly object sync = new();
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
    /// <param name="lifetime">The optional application lifetime used to stop the host when a non-retriable registration stream failure occurs.</param>
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
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (this.options.Mode != ServerlessMode.ServerlessInclude)
        {
            this.pump = Task.CompletedTask;
            return Task.CompletedTask;
        }

        string[] activityNames = ServerlessActivityConfiguration.ResolveActivityNames(this.options.ActivityNames);
        if (activityNames.Length == 0)
        {
            Logs.NoServerlessActivitiesForWorkerRegistration(this.logger, this.options.TaskHub);
            this.pump = Task.CompletedTask;
            return Task.CompletedTask;
        }

        CancellationTokenSource registrationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task registrationPump = Task.Run(
            () => this.RunRegistrationLoopAsync(activityNames.Length, registrationCts.Token),
            CancellationToken.None);
        lock (this.sync)
        {
            this.cts = registrationCts;
            this.pump = registrationPump;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? localCts;
        IServerlessActivityWorkerSession? localSession;
        Task? localPump;
        lock (this.sync)
        {
            localCts = this.cts;
            localSession = this.session;
            localPump = this.pump;
        }

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

        if (localPump is not null)
        {
            try
            {
                await localPump.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or RpcException)
            {
            }
        }

        lock (this.sync)
        {
            if (ReferenceEquals(this.cts, localCts))
            {
                this.cts = null;
            }

            if (ReferenceEquals(this.session, localSession))
            {
                this.session = null;
            }

            if (ReferenceEquals(this.pump, localPump))
            {
                this.pump = Task.CompletedTask;
            }
        }

        localCts?.Dispose();
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => new(this.StopAsync(CancellationToken.None));

    async Task RunRegistrationLoopAsync(int activityCount, CancellationToken cancellationToken)
    {
        TimeSpan retryDelay = this.GetInitialRetryDelay();
        while (!cancellationToken.IsCancellationRequested)
        {
            IServerlessActivityWorkerSession? registrationSession = null;
            try
            {
                registrationSession = this.client.OpenServerlessActivityWorkerSession(this.options.TaskHub, cancellationToken);
                this.SetCurrentSession(registrationSession);

                Proto.ServerlessActivityWorkerMessage startMessage = ServerlessActivityConfiguration.BuildWorkerStart(this.options);
                await registrationSession.WriteMessageAsync(startMessage).ConfigureAwait(false);
                Logs.ServerlessActivityWorkerRegistered(
                    this.logger,
                    startMessage.Start.TaskHub,
                    startMessage.Start.WorkerInstanceId,
                    activityCount,
                    startMessage.Start.Substrate,
                    startMessage.Start.SandboxId);

                retryDelay = this.GetInitialRetryDelay();
                await this.PumpHeartbeatsAsync(registrationSession, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (!IsRetriableRegistrationFailure(ex))
            {
                Logs.ServerlessActivityWorkerRegistrationFailed(this.logger, ex, this.options.TaskHub);
                this.lifetime?.StopApplication();
                break;
            }
            catch (Exception ex)
            {
                Logs.ServerlessActivityWorkerRegistrationFailed(this.logger, ex, this.options.TaskHub);
                await DelayBeforeReconnectAsync(retryDelay, cancellationToken).ConfigureAwait(false);
                retryDelay = this.GetNextRetryDelay(retryDelay);
            }
            finally
            {
                if (registrationSession is not null)
                {
                    this.ClearCurrentSession(registrationSession);
                    await DisposeSessionAsync(registrationSession).ConfigureAwait(false);
                }
            }
        }
    }

    async Task PumpHeartbeatsAsync(
        IServerlessActivityWorkerSession registrationSession,
        CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(this.options.HeartbeatInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            int activeActivitiesCount = this.activityTracker?.InFlightCount ?? 0;
            await registrationSession.WriteMessageAsync(
                ServerlessActivityConfiguration.BuildWorkerHeartbeat(activeActivitiesCount)).ConfigureAwait(false);
        }
    }

    void SetCurrentSession(IServerlessActivityWorkerSession registrationSession)
    {
        lock (this.sync)
        {
            this.session = registrationSession;
        }
    }

    void ClearCurrentSession(IServerlessActivityWorkerSession registrationSession)
    {
        lock (this.sync)
        {
            if (ReferenceEquals(this.session, registrationSession))
            {
                this.session = null;
            }
        }
    }

    TimeSpan GetInitialRetryDelay() =>
        this.options.WorkerRegistrationRetryInitialDelay <= this.options.WorkerRegistrationRetryMaxDelay
            ? this.options.WorkerRegistrationRetryInitialDelay
            : this.options.WorkerRegistrationRetryMaxDelay;

    TimeSpan GetNextRetryDelay(TimeSpan retryDelay)
    {
        if (retryDelay <= TimeSpan.Zero)
        {
            return retryDelay;
        }

        long nextTicks = Math.Min(retryDelay.Ticks * 2, this.options.WorkerRegistrationRetryMaxDelay.Ticks);
        return TimeSpan.FromTicks(nextTicks);
    }

    static async Task DelayBeforeReconnectAsync(TimeSpan retryDelay, CancellationToken cancellationToken)
    {
        if (retryDelay > TimeSpan.Zero)
        {
            await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    static async ValueTask DisposeSessionAsync(IServerlessActivityWorkerSession registrationSession)
    {
        try
        {
            await registrationSession.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or RpcException)
        {
        }
    }

    static bool IsRetriableRegistrationFailure(Exception exception) =>
        exception is OperationCanceledException or ObjectDisposedException or IOException
        || exception is RpcException rpcException
            && rpcException.StatusCode is StatusCode.Cancelled
                or StatusCode.DeadlineExceeded
                or StatusCode.Internal
                or StatusCode.ResourceExhausted
                or StatusCode.Unavailable
                or StatusCode.Unknown;
}
