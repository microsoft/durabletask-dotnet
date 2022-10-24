// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
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
    readonly IServiceProvider services;
    readonly ILoggerFactory loggerFactory;
    readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcDurableTaskWorker" /> class.
    /// </summary>
    /// <param name="name">The name of the worker.</param>
    /// <param name="factory">The task factory.</param>
    /// <param name="options">The common worker options.</param>
    /// <param name="grpcOptions">The gRPC worker options.</param>
    /// <param name="services">The service provider.</param>
    /// <param name="loggerFactory">The logger.</param>
    public GrpcDurableTaskWorker(
        string name,
        DurableTaskFactory factory,
        DurableTaskWorkerOptions options,
        IOptionsMonitor<GrpcDurableTaskWorkerOptions> grpcOptions,
        IServiceProvider services,
        ILoggerFactory loggerFactory)
        : base(name, factory, options)
    {
        this.grpcOptions = Check.NotNull(grpcOptions).Get(name);
        this.services = Check.NotNull(services);
        this.loggerFactory = Check.NotNull(loggerFactory);
        this.logger = loggerFactory.CreateLogger("Microsoft.DurableTask"); // TODO: use better category name.
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using AsyncDisposable disposable = this.BuildChannel(out Channel channel);
        this.logger.StartingTaskHubWorker(channel.Target);
        await new Processor(this, new(channel)).ExecuteAsync(channel.Target, stoppingToken);
    }

    AsyncDisposable BuildChannel(out Channel channel)
    {
        if (this.grpcOptions.Channel is Channel c)
        {
            channel = c;
            return default;
        }

        string address = string.IsNullOrEmpty(this.grpcOptions.Address) ? "127.0.0.1:4001" : this.grpcOptions.Address!;

        // TODO: use SSL channel by default?
        c = new(address, ChannelCredentials.Insecure);
        channel = c;
        return new AsyncDisposable(async () => await c.ShutdownAsync());
    }
}
