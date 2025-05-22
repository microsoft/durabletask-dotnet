// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Dapr.DurableTask.Worker.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dapr.DurableTask.Worker.Grpc;

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

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcDurableTaskWorker" /> class.
    /// </summary>
    /// <param name="name">The name of the worker.</param>
    /// <param name="factory">The task factory.</param>
    /// <param name="grpcOptions">The gRPC-specific worker options.</param>
    /// <param name="workerOptions">The generic worker options.</param>
    /// <param name="services">The service provider.</param>
    /// <param name="loggerFactory">The logger.</param>
    public GrpcDurableTaskWorker(
        string name,
        IDurableTaskFactory factory,
        IOptionsMonitor<GrpcDurableTaskWorkerOptions> grpcOptions,
        IOptionsMonitor<DurableTaskWorkerOptions> workerOptions,
        IServiceProvider services,
        ILoggerFactory loggerFactory)
        : base(name, factory)
    {
        this.grpcOptions = Check.NotNull(grpcOptions).Get(name);
        this.workerOptions = Check.NotNull(workerOptions).Get(name);
        this.services = Check.NotNull(services);
        this.loggerFactory = Check.NotNull(loggerFactory);
        this.logger = loggerFactory.CreateLogger("Microsoft.DurableTask"); // TODO: use better category name.
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using AsyncDisposable disposable = this.GetCallInvoker(out CallInvoker callInvoker, out string address);
        this.logger.StartingTaskHubWorker(address);
        await new Processor(this, new(callInvoker)).ExecuteAsync(stoppingToken);
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
}
