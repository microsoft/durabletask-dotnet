// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proto = Microsoft.DurableTask.Protobuf.OnDemandSandbox;

namespace Microsoft.DurableTask.Worker.AzureManaged.OnDemandSandbox;

/// <summary>
/// Hosted service that registers a running process as an on-demand sandbox activity worker with DTS.
/// </summary>
sealed class OnDemandSandboxActivityWorkerRegistrationHostedService : IHostedService, IAsyncDisposable
{
    readonly object sync = new();
    readonly IOnDemandSandboxActivitiesTransport transport;
    readonly OnDemandSandboxWorkerRuntimeOptions options;
    readonly IReadOnlyCollection<string> registeredActivityNames;
    readonly ILogger<OnDemandSandboxActivityWorkerRegistrationHostedService> logger;
    readonly IHostApplicationLifetime? lifetime;
    readonly OnDemandSandboxActivityTracker? activityTracker;
    readonly Random reconnectJitter;
    readonly SemaphoreSlim streamSync = new(1, 1);
    CancellationTokenSource? cts;
    IOnDemandSandboxActivityWorkerSession? session;
    Task? pump;

    /// <summary>
    /// Initializes a new instance of the <see cref="OnDemandSandboxActivityWorkerRegistrationHostedService"/> class.
    /// </summary>
    /// <param name="transport">The on-demand sandbox activities transport.</param>
    /// <param name="options">The on-demand sandbox worker runtime options.</param>
    /// <param name="registeredActivityNames">The activity handlers registered by this worker process.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="lifetime">The optional application lifetime used to stop the host when a non-retriable registration stream failure occurs.</param>
    /// <param name="activityTracker">The optional activity tracker used to report live in-flight activity count.</param>
    /// <param name="reconnectJitter">The optional random source used to jitter reconnect delays.</param>
    public OnDemandSandboxActivityWorkerRegistrationHostedService(
        IOnDemandSandboxActivitiesTransport transport,
        OnDemandSandboxWorkerRuntimeOptions options,
        IReadOnlyCollection<string> registeredActivityNames,
        ILogger<OnDemandSandboxActivityWorkerRegistrationHostedService> logger,
        IHostApplicationLifetime? lifetime = null,
        OnDemandSandboxActivityTracker? activityTracker = null,
        Random? reconnectJitter = null)
    {
        this.transport = Check.NotNull(transport);
        this.options = Check.NotNull(options);
        this.registeredActivityNames = Check.NotNull(registeredActivityNames);
        this.logger = Check.NotNull(logger);
        this.lifetime = lifetime;
        this.activityTracker = activityTracker;
        this.reconnectJitter = reconnectJitter ?? Random.Shared;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (this.options.Mode != OnDemandSandboxMode.OnDemandSandboxInclude)
        {
            this.pump = Task.CompletedTask;
            return Task.CompletedTask;
        }

        string[] activityNames = OnDemandSandboxActivityDeclarationBuilder.ResolveActivityNames(this.registeredActivityNames);
        if (activityNames.Length == 0)
        {
            Logs.NoOnDemandSandboxActivitiesForWorkerRegistration(this.logger, this.options.TaskHub);
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
        IOnDemandSandboxActivityWorkerSession? localSession;
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
                await this.CompleteSessionAsync(localSession, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or RpcException)
            {
                Logs.OnDemandSandboxWorkerSessionCompletionFailureIgnored(this.logger, ex);
            }
        }

        if (localPump is not null)
        {
            try
            {
                await localPump.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                Logs.OnDemandSandboxWorkerRegistrationPumpCancellationIgnored(this.logger, ex);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or RpcException)
            {
                Logs.OnDemandSandboxWorkerRegistrationPumpFailureIgnored(this.logger, ex);
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

    /// <summary>
    /// Computes a full-jitter reconnect delay in the range <c>[0, retryDelay)</c>.
    /// </summary>
    /// <param name="retryDelay">The current exponential retry delay.</param>
    /// <param name="random">The random source used for jitter.</param>
    /// <returns>The jittered reconnect delay.</returns>
    internal static TimeSpan ComputeJitteredReconnectDelay(TimeSpan retryDelay, Random random)
    {
        Check.NotNull(random);
        if (retryDelay <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        long jitteredTicks = (long)(random.NextDouble() * retryDelay.Ticks);
        return TimeSpan.FromTicks(jitteredTicks);
    }

    static async ValueTask DisposeSessionAsync(
        IOnDemandSandboxActivityWorkerSession registrationSession,
        ILogger logger)
    {
        try
        {
            await registrationSession.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or RpcException)
        {
            Logs.OnDemandSandboxWorkerSessionDisposeFailureIgnored(logger, ex);
        }
    }

    static bool IsRetriableRegistrationFailure(Exception exception) =>
        (exception is OperationCanceledException or ObjectDisposedException or IOException)
        || (exception is RpcException rpcException
            && rpcException.StatusCode is StatusCode.Cancelled
                or StatusCode.DeadlineExceeded
                or StatusCode.Internal
                or StatusCode.ResourceExhausted
                or StatusCode.Unavailable
                or StatusCode.Unknown);

    async Task RunRegistrationLoopAsync(int activityCount, CancellationToken cancellationToken)
    {
        TimeSpan retryDelay = this.GetInitialRetryDelay();
        while (!cancellationToken.IsCancellationRequested)
        {
            IOnDemandSandboxActivityWorkerSession? registrationSession = null;
            try
            {
                registrationSession = this.transport.OpenOnDemandSandboxActivityWorkerSession(this.options.TaskHub, cancellationToken);
                this.SetCurrentSession(registrationSession);

                Proto.OnDemandSandboxActivityWorkerMessage startMessage = OnDemandSandboxWorkerMessageBuilder.BuildWorkerStart(this.options, this.registeredActivityNames);
                await this.WriteSessionMessageAsync(registrationSession, startMessage, cancellationToken).ConfigureAwait(false);
                Logs.OnDemandSandboxActivityWorkerRegistered(
                    this.logger,
                    startMessage.Start.TaskHub,
                    activityCount,
                    startMessage.Start.Substrate,
                    startMessage.Start.DtsSandboxIdentifier);

                retryDelay = this.GetInitialRetryDelay();
                await this.RunRegistrationSessionAsync(registrationSession, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (RpcException ex) when (IsRetriableRegistrationFailure(ex))
            {
                retryDelay = await this.HandleRetriableRegistrationFailureAsync(
                    ex,
                    retryDelay,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex) when (IsRetriableRegistrationFailure(ex))
            {
                retryDelay = await this.HandleRetriableRegistrationFailureAsync(
                    ex,
                    retryDelay,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (ObjectDisposedException ex) when (IsRetriableRegistrationFailure(ex))
            {
                retryDelay = await this.HandleRetriableRegistrationFailureAsync(
                    ex,
                    retryDelay,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (IsRetriableRegistrationFailure(ex))
            {
                retryDelay = await this.HandleRetriableRegistrationFailureAsync(
                    ex,
                    retryDelay,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logs.OnDemandSandboxActivityWorkerRegistrationFailed(this.logger, ex, this.options.TaskHub);
                this.lifetime?.StopApplication();
                break;
            }
            finally
            {
                if (registrationSession is not null)
                {
                    this.ClearCurrentSession(registrationSession);
                    await DisposeSessionAsync(registrationSession, this.logger).ConfigureAwait(false);
                }
            }
        }
    }

    async Task<TimeSpan> HandleRetriableRegistrationFailureAsync(
        Exception exception,
        TimeSpan retryDelay,
        CancellationToken cancellationToken)
    {
        Logs.OnDemandSandboxActivityWorkerRegistrationFailed(this.logger, exception, this.options.TaskHub);
        await this.DelayBeforeReconnectAsync(retryDelay, cancellationToken).ConfigureAwait(false);
        return this.GetNextRetryDelay(retryDelay);
    }

    async Task RunRegistrationSessionAsync(
        IOnDemandSandboxActivityWorkerSession registrationSession,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task heartbeatTask = this.PumpHeartbeatsAsync(registrationSession, heartbeatCts.Token);
        Task<Proto.OnDemandSandboxActivityWorkerSessionResult> completionTask = registrationSession.WaitForCompletionAsync();
        Task completedTask = await Task.WhenAny(heartbeatTask, completionTask).ConfigureAwait(false);

        if (ReferenceEquals(completedTask, completionTask))
        {
            heartbeatCts.Cancel();
            try
            {
                await heartbeatTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (heartbeatCts.IsCancellationRequested)
            {
                Logs.OnDemandSandboxHeartbeatPumpCancellationIgnored(this.logger, ex);
            }
            catch (RpcException ex)
            {
                // The server response is authoritative once the response task wins the race.
                Logs.OnDemandSandboxHeartbeatPumpFailureIgnored(this.logger, ex);
            }
            catch (IOException ex)
            {
                // The server response is authoritative once the response task wins the race.
                Logs.OnDemandSandboxHeartbeatPumpFailureIgnored(this.logger, ex);
            }
            catch (ObjectDisposedException ex)
            {
                // The server response is authoritative once the response task wins the race.
                Logs.OnDemandSandboxHeartbeatPumpFailureIgnored(this.logger, ex);
            }

            await completionTask.ConfigureAwait(false);
            return;
        }

        await heartbeatTask.ConfigureAwait(false);
    }

    async Task PumpHeartbeatsAsync(
        IOnDemandSandboxActivityWorkerSession registrationSession,
        CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(this.options.HeartbeatInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            int activeActivitiesCount = this.activityTracker?.InFlightCount ?? 0;
            await this.WriteSessionMessageAsync(
                registrationSession,
                OnDemandSandboxWorkerMessageBuilder.BuildWorkerHeartbeat(activeActivitiesCount),
                cancellationToken).ConfigureAwait(false);
        }
    }

    async Task WriteSessionMessageAsync(
        IOnDemandSandboxActivityWorkerSession registrationSession,
        Proto.OnDemandSandboxActivityWorkerMessage message,
        CancellationToken cancellationToken)
    {
        await this.streamSync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await registrationSession.WriteMessageAsync(message).ConfigureAwait(false);
        }
        finally
        {
            this.streamSync.Release();
        }
    }

    async Task CompleteSessionAsync(
        IOnDemandSandboxActivityWorkerSession registrationSession,
        CancellationToken cancellationToken)
    {
        await this.streamSync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await registrationSession.CompleteAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            this.streamSync.Release();
        }
    }

    void SetCurrentSession(IOnDemandSandboxActivityWorkerSession registrationSession)
    {
        lock (this.sync)
        {
            this.session = registrationSession;
        }
    }

    void ClearCurrentSession(IOnDemandSandboxActivityWorkerSession registrationSession)
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

    async Task DelayBeforeReconnectAsync(TimeSpan retryDelay, CancellationToken cancellationToken)
    {
        TimeSpan jitteredDelay = ComputeJitteredReconnectDelay(retryDelay, this.reconnectJitter);
        if (jitteredDelay > TimeSpan.Zero)
        {
            await Task.Delay(jitteredDelay, cancellationToken).ConfigureAwait(false);
        }
    }
}
