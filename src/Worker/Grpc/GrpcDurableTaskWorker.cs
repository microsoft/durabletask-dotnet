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
    readonly GrpcDurableTaskWorkerOptions options;
    readonly IServiceProvider services;
    readonly ILoggerFactory loggerFactory;
    readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcDurableTaskWorker" /> class.
    /// </summary>
    /// <param name="name">The name of the worker.</param>
    /// <param name="factory">The task factory.</param>
    /// <param name="options">The gRPC worker options.</param>
    /// <param name="services">The service provider.</param>
    /// <param name="loggerFactory">The logger.</param>
    public GrpcDurableTaskWorker(
        string name,
        IDurableTaskFactory factory,
        IOptionsMonitor<GrpcDurableTaskWorkerOptions> options,
        IServiceProvider services,
        ILoggerFactory loggerFactory)
        : base(name, factory)
    {
        this.options = Check.NotNull(options).Get(name);
        this.services = Check.NotNull(services);
        this.loggerFactory = Check.NotNull(loggerFactory);
        this.logger = loggerFactory.CreateLogger("Microsoft.DurableTask"); // TODO: use better category name.
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using AsyncDisposable disposable = this.BuildChannel(out GrpcChannel channel);
        this.logger.StartingTaskHubWorker(channel.Target);
        await new Processor(this, new(channel)).ExecuteAsync(channel.Target, stoppingToken);
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

    AsyncDisposable BuildChannel(out GrpcChannel channel)
    {
        if (this.options.Channel is GrpcChannel c)
        {
            channel = c;
            return default;
        }

        c = GetChannel(this.options.Address);
        channel = c;
        return new AsyncDisposable(async () => await c.ShutdownAsync());
    }
}
