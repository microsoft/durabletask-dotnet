// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using Grpc.Core;
using Microsoft.DurableTask.AzureManaged.Internal;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proto = Microsoft.DurableTask.Protobuf.Sandboxes;

namespace Microsoft.DurableTask.Worker.AzureManaged.Sandboxes;

/// <summary>
/// Hosted service that registers a running process as an on-demand sandbox activity worker with DTS.
/// </summary>
sealed class SandboxActivityWorkerRegistrationHostedService : IHostedService, IAsyncDisposable
{
    readonly object sync = new();
    readonly ISandboxActivitiesTransport transport;
    readonly SandboxWorkerRuntimeOptions options;
    readonly IReadOnlyCollection<SandboxActivityMetadata.Activity> registeredActivities;
    readonly ILogger<SandboxActivityWorkerRegistrationHostedService> logger;
    readonly IHostApplicationLifetime? lifetime;
    readonly SandboxActivityTracker? activityTracker;
    readonly Random reconnectJitter;
    readonly SemaphoreSlim streamSync = new(1, 1);
    CancellationTokenSource? cts;
    ISandboxActivityWorkerSession? session;
    Task? registrationLoopTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="SandboxActivityWorkerRegistrationHostedService"/> class.
    /// </summary>
    /// <param name="transport">The on-demand sandbox activities transport.</param>
    /// <param name="options">The on-demand sandbox worker runtime options.</param>
    /// <param name="registeredActivities">The activity handlers registered by this worker process.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="lifetime">The optional application lifetime used to stop the host when a non-retriable registration stream failure occurs.</param>
    /// <param name="activityTracker">The optional activity tracker used to report live in-flight activity count.</param>
    /// <param name="reconnectJitter">The optional random source used to jitter reconnect delays.</param>
    public SandboxActivityWorkerRegistrationHostedService(
        ISandboxActivitiesTransport transport,
        SandboxWorkerRuntimeOptions options,
        IReadOnlyCollection<SandboxActivityMetadata.Activity> registeredActivities,
        ILogger<SandboxActivityWorkerRegistrationHostedService> logger,
        IHostApplicationLifetime? lifetime = null,
        SandboxActivityTracker? activityTracker = null,
        Random? reconnectJitter = null)
    {
        this.transport = Check.NotNull(transport);
        this.options = Check.NotNull(options);
        this.registeredActivities = Check.NotNull(registeredActivities);
        this.logger = Check.NotNull(logger);
        this.lifetime = lifetime;
        this.activityTracker = activityTracker;
        this.reconnectJitter = reconnectJitter ?? Random.Shared;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        SandboxActivityMetadata.Activity[] activities = SandboxActivityMetadata.ResolveActivities(this.registeredActivities);
        CancellationTokenSource registrationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task startedRegistrationLoopTask = Task.Run(
            () => this.RunRegistrationLoopAsync(activities.Length, registrationCts.Token),
            CancellationToken.None);
        lock (this.sync)
        {
            this.cts = registrationCts;
            this.registrationLoopTask = startedRegistrationLoopTask;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? localCts;
        Task? localRegistrationLoopTask;
        lock (this.sync)
        {
            localCts = this.cts;
            localRegistrationLoopTask = this.registrationLoopTask;
        }

        localCts?.Cancel();

        if (localRegistrationLoopTask is not null)
        {
            try
            {
                await localRegistrationLoopTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                Logs.SandboxWorkerRegistrationLoopCancellationIgnored(this.logger, ex);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or RpcException)
            {
                Logs.SandboxWorkerRegistrationLoopFailureIgnored(this.logger, ex);
            }
        }

        lock (this.sync)
        {
            if (ReferenceEquals(this.cts, localCts))
            {
                this.cts = null;
            }

            if (ReferenceEquals(this.registrationLoopTask, localRegistrationLoopTask))
            {
                this.registrationLoopTask = Task.CompletedTask;
            }
        }

        localCts?.Dispose();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await this.StopAsync(CancellationToken.None).ConfigureAwait(false);
        this.streamSync.Dispose();
    }

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

    /// <summary>
    /// Computes the next exponential retry delay, capped at the configured maximum delay.
    /// </summary>
    /// <param name="retryDelay">The current retry delay.</param>
    /// <param name="maxDelay">The maximum retry delay.</param>
    /// <returns>The next retry delay.</returns>
    internal static TimeSpan ComputeNextRetryDelay(TimeSpan retryDelay, TimeSpan maxDelay)
    {
        if (retryDelay <= TimeSpan.Zero)
        {
            return retryDelay;
        }

        if (retryDelay >= maxDelay || retryDelay.Ticks > maxDelay.Ticks / 2)
        {
            return maxDelay;
        }

        return TimeSpan.FromTicks(retryDelay.Ticks * 2);
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

    static bool IsFatalException(Exception ex) => ex is OutOfMemoryException
        or StackOverflowException
        or AccessViolationException
        or ThreadAbortException;

    static async Task ObserveCompletionFailureAfterHeartbeatFailureAsync(
        Task<Proto.SandboxActivityWorkerSessionResult> completionTask,
        ILogger logger)
    {
        try
        {
            await completionTask.ConfigureAwait(false);
        }
        catch (Exception ex) when (!IsFatalException(ex))
        {
            Logs.SandboxWorkerSessionCompletionAfterHeartbeatFailureIgnored(logger, ex);
        }
    }

    async Task RunRegistrationLoopAsync(int activityCount, CancellationToken cancellationToken)
    {
        TimeSpan retryDelay = this.GetInitialRetryDelay();
        while (!cancellationToken.IsCancellationRequested)
        {
            ISandboxActivityWorkerSession? registrationSession = null;
            try
            {
                Proto.SandboxActivityWorkerMessage startMessage = SandboxWorkerMessageBuilder.BuildWorkerStart(this.options, this.registeredActivities);
                registrationSession = this.transport.OpenSandboxActivityWorkerSession(startMessage.Start.TaskHub, cancellationToken);
                this.SetCurrentSession(registrationSession);

                await this.WriteSessionMessageAsync(registrationSession, startMessage, cancellationToken).ConfigureAwait(false);
                Logs.SandboxActivityWorkerRegistered(
                    this.logger,
                    startMessage.Start.TaskHub,
                    activityCount,
                    startMessage.Start.SandboxProvider,
                    startMessage.Start.DtsSandboxIdentifier);

                await this.RunRegistrationSessionAsync(registrationSession, cancellationToken).ConfigureAwait(false);
                retryDelay = this.GetInitialRetryDelay();
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
            catch (Exception ex) when (!IsFatalException(ex))
            {
                Logs.SandboxActivityWorkerRegistrationFailed(this.logger, ex, this.options.TaskHub);
                this.lifetime?.StopApplication();
                break;
            }
            finally
            {
                if (registrationSession is not null)
                {
                    this.ClearCurrentSession(registrationSession);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        await this.CompleteSessionAsync(registrationSession, CancellationToken.None).ConfigureAwait(false);
                    }

                    await this.DisposeSessionAsync(registrationSession, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
    }

    async Task<TimeSpan> HandleRetriableRegistrationFailureAsync(
        Exception exception,
        TimeSpan retryDelay,
        CancellationToken cancellationToken)
    {
        Logs.SandboxActivityWorkerRegistrationFailed(this.logger, exception, this.options.TaskHub);
        await this.DelayBeforeReconnectAsync(retryDelay, cancellationToken).ConfigureAwait(false);
        return this.GetNextRetryDelay(retryDelay);
    }

    async Task RunRegistrationSessionAsync(
        ISandboxActivityWorkerSession registrationSession,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task heartbeatTask = this.RunHeartbeatLoopAsync(registrationSession, heartbeatCts.Token);
        Task<Proto.SandboxActivityWorkerSessionResult> completionTask = registrationSession.WaitForCompletionAsync();
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
                Logs.SandboxHeartbeatLoopCancellationIgnored(this.logger, ex);
            }
            catch (RpcException ex)
            {
                // The server response is authoritative once the response task wins the race.
                Logs.SandboxHeartbeatLoopFailureIgnored(this.logger, ex);
            }
            catch (IOException ex)
            {
                // The server response is authoritative once the response task wins the race.
                Logs.SandboxHeartbeatLoopFailureIgnored(this.logger, ex);
            }
            catch (ObjectDisposedException ex)
            {
                // The server response is authoritative once the response task wins the race.
                Logs.SandboxHeartbeatLoopFailureIgnored(this.logger, ex);
            }

            await completionTask.ConfigureAwait(false);
            return;
        }

        try
        {
            await heartbeatTask.ConfigureAwait(false);
        }
        finally
        {
            _ = ObserveCompletionFailureAfterHeartbeatFailureAsync(completionTask, this.logger);
        }
    }

    async Task RunHeartbeatLoopAsync(
        ISandboxActivityWorkerSession registrationSession,
        CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(this.options.HeartbeatInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            int activeActivitiesCount = this.activityTracker?.InFlightCount ?? 0;
            await this.WriteSessionMessageAsync(
                registrationSession,
                SandboxWorkerMessageBuilder.BuildWorkerHeartbeat(activeActivitiesCount),
                cancellationToken).ConfigureAwait(false);
        }
    }

    async Task WriteSessionMessageAsync(
        ISandboxActivityWorkerSession registrationSession,
        Proto.SandboxActivityWorkerMessage message,
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
        ISandboxActivityWorkerSession registrationSession,
        CancellationToken cancellationToken)
    {
        await this.WithSessionStreamLockAsync(
            registrationSession.CompleteAsync,
            Logs.SandboxWorkerSessionCompletionFailureIgnored,
            cancellationToken).ConfigureAwait(false);
    }

    async ValueTask DisposeSessionAsync(
        ISandboxActivityWorkerSession registrationSession,
        CancellationToken cancellationToken)
    {
        await this.WithSessionStreamLockAsync(
            async () => await registrationSession.DisposeAsync().ConfigureAwait(false),
            Logs.SandboxWorkerSessionDisposeFailureIgnored,
            cancellationToken).ConfigureAwait(false);
    }

    async Task WithSessionStreamLockAsync(
        Func<Task> sessionOperation,
        Action<ILogger, Exception> logIgnoredFailure,
        CancellationToken cancellationToken)
    {
        await this.streamSync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await sessionOperation().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or RpcException)
        {
            logIgnoredFailure(this.logger, ex);
        }
        finally
        {
            this.streamSync.Release();
        }
    }

    void SetCurrentSession(ISandboxActivityWorkerSession registrationSession)
    {
        lock (this.sync)
        {
            this.session = registrationSession;
        }
    }

    void ClearCurrentSession(ISandboxActivityWorkerSession registrationSession)
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

    TimeSpan GetNextRetryDelay(TimeSpan retryDelay) =>
        ComputeNextRetryDelay(retryDelay, this.options.WorkerRegistrationRetryMaxDelay);

    async Task DelayBeforeReconnectAsync(TimeSpan retryDelay, CancellationToken cancellationToken)
    {
        TimeSpan jitteredDelay = ComputeJitteredReconnectDelay(retryDelay, this.reconnectJitter);
        if (jitteredDelay > TimeSpan.Zero)
        {
            await Task.Delay(jitteredDelay, cancellationToken).ConfigureAwait(false);
        }
    }
}
