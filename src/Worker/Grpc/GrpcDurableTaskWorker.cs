// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using Microsoft.DurableTask.Worker.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Worker.Grpc;

/// <summary>
/// The gRPC Durable Task worker.
/// </summary>
sealed partial class GrpcDurableTaskWorker : DurableTaskWorker
{
    static TimeSpan deferredDisposeGracePeriod = TimeSpan.FromSeconds(30);

    readonly GrpcDurableTaskWorkerOptions grpcOptions;
    readonly DurableTaskWorkerOptions workerOptions;
    readonly IServiceProvider services;
    readonly ILoggerFactory loggerFactory;
    readonly ILogger logger;
    readonly IOrchestrationFilter? orchestrationFilter;
    readonly DurableTaskWorkerWorkItemFilters? workItemFilters;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcDurableTaskWorker" /> class.
    /// </summary>
    /// <param name="name">The name of the worker.</param>
    /// <param name="factory">The task factory.</param>
    /// <param name="grpcOptions">The gRPC-specific worker options.</param>
    /// <param name="workerOptions">The generic worker options.</param>
    /// <param name="services">The service provider.</param>
    /// <param name="loggerFactory">The logger.</param>
    /// <param name="orchestrationFilter">The optional <see cref="IOrchestrationFilter"/> used to filter orchestration execution.</param>
    /// <param name="exceptionPropertiesProvider">The custom exception properties provider that help build failure details.</param>
    /// <param name="workItemFiltersMonitor">The optional <see cref="IOptionsMonitor{DurableTaskWorkerWorkItemFilters}"/> used to filter work items in the backend.</param>
    public GrpcDurableTaskWorker(
        string name,
        IDurableTaskFactory factory,
        IOptionsMonitor<GrpcDurableTaskWorkerOptions> grpcOptions,
        IOptionsMonitor<DurableTaskWorkerOptions> workerOptions,
        IServiceProvider services,
        ILoggerFactory loggerFactory,
        IOrchestrationFilter? orchestrationFilter = null,
        IExceptionPropertiesProvider? exceptionPropertiesProvider = null,
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters>? workItemFiltersMonitor = null)
        : base(name, factory)
    {
        this.grpcOptions = Check.NotNull(grpcOptions).Get(name);
        this.workerOptions = Check.NotNull(workerOptions).Get(name);
        this.services = Check.NotNull(services);
        this.loggerFactory = Check.NotNull(loggerFactory);
        this.logger = CreateLogger(loggerFactory, this.workerOptions);
        this.orchestrationFilter = orchestrationFilter;
        this.ExceptionPropertiesProvider = exceptionPropertiesProvider;
        this.workItemFilters = workItemFiltersMonitor?.Get(name);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        AsyncDisposable workerOwnedChannelDisposable = this.GetCallInvoker(out CallInvoker callInvoker, out string address);

        // Seed the tracker from the configured channel once, then update latestObservedChannel after
        // each successful recreate. Do not re-read this.grpcOptions.Channel inside the loop: the options
        // object keeps its original Channel reference even when a shared backend-channel cache has already
        // swapped to a newer instance.
        GrpcChannel? latestObservedChannel = this.grpcOptions.Channel;
        try
        {
            this.logger.StartingTaskHubWorker(address);

            while (!stoppingToken.IsCancellationRequested)
            {
                Processor processor = new(this, new(callInvoker), this.orchestrationFilter, this.ExceptionPropertiesProvider);
                ProcessorExitReason reason = await processor.ExecuteAsync(stoppingToken);

                if (reason == ProcessorExitReason.Shutdown || stoppingToken.IsCancellationRequested)
                {
                    return;
                }

                // ChannelRecreateRequested: try to obtain a fresh channel before re-entering the loop.
                ChannelRecreateResult result = await this.TryRecreateChannelAsync(stoppingToken, workerOwnedChannelDisposable, latestObservedChannel);
                if (result.Recreated)
                {
                    this.ApplySuccessfulRecreate(
                        result,
                        ref callInvoker,
                        ref address,
                        ref latestObservedChannel,
                        ref workerOwnedChannelDisposable);
                }

                // If we couldn't recreate (e.g., caller-owned CallInvoker), fall through and retry on the
                // existing transport. The Processor's outer backoff already waited before returning.
            }
        }
        finally
        {
            await workerOwnedChannelDisposable.DisposeAsync();
        }
    }

    async Task<ChannelRecreateResult> TryRecreateChannelAsync(
        CancellationToken cancellation,
        AsyncDisposable currentWorkerOwnedDisposable,
        GrpcChannel? currentChannel)
    {
        // There are three ownership models here:
        // 1. A caller-supplied recreator owns shared/cache-backed channels and decides how to swap them.
        // 2. The worker owns an Address-created channel and can rebuild it directly.
        // 3. The caller owns the Channel/CallInvoker, so the worker can only keep retrying on the same transport.

        // Path 1: caller (or extension method like ConfigureGrpcChannel) supplied a recreator.
        Func<GrpcChannel, CancellationToken, Task<GrpcChannel>>? recreator = this.grpcOptions.Internal.ChannelRecreator;
        if (recreator is not null && currentChannel is not null)
        {
            try
            {
                GrpcChannel newChannel = await recreator(currentChannel, cancellation).ConfigureAwait(false);
                if (!ReferenceEquals(newChannel, currentChannel))
                {
                    // The recreator owns the replacement channel lifetime. Return a default disposable
                    // so the caller disposes the previous worker-owned channel exactly once without
                    // carrying that ownership forward to the recreated state.
                    return new ChannelRecreateResult(true, newChannel.CreateCallInvoker(), newChannel.Target, default, newChannel);
                }

                // Recreator returned the same instance — nothing to swap.
                return ChannelRecreateResult.NotRecreated(currentWorkerOwnedDisposable);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (!IsFatal(ex))
            {
                // Don't crash the worker if recreate fails; just keep using the existing transport.
                this.logger.UnexpectedError(ex, string.Empty);
                return ChannelRecreateResult.NotRecreated(currentWorkerOwnedDisposable);
            }
        }

        // Path 2: worker-owned channel created from Address. We can rebuild it ourselves.
        if (this.grpcOptions.Channel is null
            && this.grpcOptions.CallInvoker is null)
        {
            try
            {
                GrpcChannel newChannel = GetChannel(this.grpcOptions.Address);
                // This new channel is worker-owned, so hand back a disposable that will shut it down
                // (and dispose it on frameworks where GrpcChannel implements IDisposable).
                AsyncDisposable newDisposable = CreateOwnedChannelDisposable(newChannel);
                return new ChannelRecreateResult(true, newChannel.CreateCallInvoker(), newChannel.Target, newDisposable, newChannel);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (!IsFatal(ex))
            {
                this.logger.UnexpectedError(ex, string.Empty);
                return ChannelRecreateResult.NotRecreated(currentWorkerOwnedDisposable);
            }
        }

        // Path 3: caller-owned CallInvoker or externally-supplied Channel without a recreator.
        // No safe way to recreate; let the inner loop continue trying on the existing transport.
        return ChannelRecreateResult.NotRecreated(currentWorkerOwnedDisposable);

        static bool IsFatal(Exception ex) => ex is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or ThreadAbortException;
    }

    readonly struct ChannelRecreateResult
    {
        public ChannelRecreateResult(bool recreated, CallInvoker? newCallInvoker, string? newAddress, AsyncDisposable newWorkerOwnedDisposable, GrpcChannel? newChannel)
        {
            this.Recreated = recreated;
            this.NewCallInvoker = newCallInvoker;
            this.NewAddress = newAddress;
            this.NewWorkerOwnedDisposable = newWorkerOwnedDisposable;
            this.NewChannel = newChannel;
        }

        public bool Recreated { get; }

        public CallInvoker? NewCallInvoker { get; }

        public string? NewAddress { get; }

        public AsyncDisposable NewWorkerOwnedDisposable { get; }

        public GrpcChannel? NewChannel { get; }

        public static ChannelRecreateResult NotRecreated(AsyncDisposable currentDisposable)
            => new(false, null, null, currentDisposable, null);
    }

#if NET6_0_OR_GREATER
    static GrpcChannel GetChannel(string? address)
    {
        if (string.IsNullOrEmpty(address))
        {
            address = "http://localhost:4001";
        }

        return GrpcChannel.ForAddress(address);
    }
#endif

#if NETSTANDARD2_0
    static GrpcChannel GetChannel(string? address)
    {
        if (string.IsNullOrEmpty(address))
        {
            address = "localhost:4001";
        }

        return new(address, ChannelCredentials.Insecure);
    }
#endif

    static AsyncDisposable CreateOwnedChannelDisposable(GrpcChannel channel)
    {
        return new AsyncDisposable(() => ShutdownAndDisposeChannelAsync(channel));
    }

    static async ValueTask ShutdownAndDisposeChannelAsync(GrpcChannel channel)
    {
        try
        {
            await channel.ShutdownAsync().ConfigureAwait(false);
        }
        finally
        {
#if NET6_0_OR_GREATER
            channel.Dispose();
#endif
        }
    }

    void ApplySuccessfulRecreate(
        ChannelRecreateResult result,
        ref CallInvoker callInvoker,
        ref string address,
        ref GrpcChannel? latestObservedChannel,
        ref AsyncDisposable workerOwnedChannelDisposable)
    {
        callInvoker = result.NewCallInvoker!;
        address = result.NewAddress!;
        latestObservedChannel = result.NewChannel;
        AsyncDisposable previousDisposable = workerOwnedChannelDisposable;
        workerOwnedChannelDisposable = result.NewWorkerOwnedDisposable;
        this.logger.ChannelRecreated(address);

        // Defer disposal of the prior worker-owned channel so background completion/abandon RPCs
        // from the previous processor instance can drain before the transport is torn down.
        // Path 1 hands ownership of the replacement channel to the recreator, Path 2 installs a
        // fresh worker-owned disposable, and Path 3 never recreates at all.
        _ = ScheduleDeferredDisposeAsync(previousDisposable, deferredDisposeGracePeriod);
    }

    static async Task ScheduleDeferredDisposeAsync(AsyncDisposable disposable, TimeSpan delay)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay).ConfigureAwait(false);
            }

            await disposable.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException
                                    and not StackOverflowException
                                    and not AccessViolationException
                                    and not ThreadAbortException)
        {
            if (ex is not OperationCanceledException and not ObjectDisposedException)
            {
                Trace.TraceError(
                    "Unexpected exception while deferred-disposing gRPC channel in GrpcDurableTaskWorker.ScheduleDeferredDisposeAsync: {0}",
                    ex);
            }
        }
    }

    AsyncDisposable GetCallInvoker(out CallInvoker callInvoker, out string address)
    {
        if (this.grpcOptions.Channel is GrpcChannel c)
        {
            callInvoker = c.CreateCallInvoker();
            address = c.Target;
            return default;
        }

        if (this.grpcOptions.CallInvoker is CallInvoker invoker)
        {
            callInvoker = invoker;
            address = "(unspecified)";
            return default;
        }

        c = GetChannel(this.grpcOptions.Address);
        callInvoker = c.CreateCallInvoker();
        address = c.Target;
        return CreateOwnedChannelDisposable(c);
    }

    static ILogger CreateLogger(ILoggerFactory loggerFactory, DurableTaskWorkerOptions options)
    {
        // Use the new, more specific category name for gRPC worker logs
        ILogger primaryLogger = loggerFactory.CreateLogger("Microsoft.DurableTask.Worker.Grpc");

        // If legacy categories are enabled, also emit logs to the old broad category
        if (options.Logging.UseLegacyCategories)
        {
            ILogger legacyLogger = loggerFactory.CreateLogger("Microsoft.DurableTask");
            return new DualCategoryLogger(primaryLogger, legacyLogger);
        }

        return primaryLogger;
    }
}
