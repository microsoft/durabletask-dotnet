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
        await using AsyncDisposable disposable = this.GetCallInvoker(out CallInvoker callInvoker, out string address);
        this.logger.StartingTaskHubWorker(address);
        await new Processor(this, new(callInvoker), this.orchestrationFilter, this.ExceptionPropertiesProvider).ExecuteAsync(stoppingToken);
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
