// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Worker.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Worker.Grpc;

/// <summary>
/// The gRPC Durable Task worker.
/// </summary>
sealed partial class GrpcDurableTaskWorker : DurableTaskWorker
{
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

        // Track the most recently observed channel so the recreator can compare it against the
        // currently-cached channel and skip the swap when a peer worker has already recreated it.
        // We must NOT use this.grpcOptions.Channel here: that field is set once when options are
        // configured and is never updated when the AzureManaged extension swaps the cached channel.
        // Passing the stale field would cause the recreator's "peer already swapped" branch to be
        // skipped, producing redundant ChannelRecreated logs and wasted recreate attempts.
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
                    callInvoker = result.NewCallInvoker!;
                    address = result.NewAddress!;
                    latestObservedChannel = result.NewChannel;
                    AsyncDisposable previousDisposable = workerOwnedChannelDisposable;
                    workerOwnedChannelDisposable = result.NewWorkerOwnedDisposable;
                    this.logger.ChannelRecreated(address);

                    // Dispose the prior worker-owned channel (if any). For Path 1 (caller-supplied recreator)
                    // and Path 3 (caller-owned), the previous disposable is a default AsyncDisposable whose
                    // DisposeAsync is a no-op, so this is always safe. We do not use ReferenceEquals here
                    // because AsyncDisposable is a value type and reference comparison is meaningless.
                    await previousDisposable.DisposeAsync();
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
        // Path 1: caller (or extension method like ConfigureGrpcChannel) supplied a recreator.
        Func<GrpcChannel, CancellationToken, Task<GrpcChannel>>? recreator = this.grpcOptions.Internal.ChannelRecreator;
        if (recreator is not null && currentChannel is not null)
        {
            try
            {
                GrpcChannel newChannel = await recreator(currentChannel, cancellation).ConfigureAwait(false);
                if (!ReferenceEquals(newChannel, currentChannel))
                {
                    // The recreator owns disposal of the old channel; we don't dispose here.
                    return new ChannelRecreateResult(true, newChannel.CreateCallInvoker(), newChannel.Target, currentWorkerOwnedDisposable, newChannel);
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
                AsyncDisposable newDisposable = new(() => new(newChannel.ShutdownAsync()));
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
        return new AsyncDisposable(() => new(c.ShutdownAsync()));
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
