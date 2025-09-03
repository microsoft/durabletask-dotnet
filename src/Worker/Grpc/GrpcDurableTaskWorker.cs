// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Dapr.DurableTask.Worker.Hosting;
using Grpc.Net.Client.Configuration;
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
    readonly IHttpClientFactory? httpClientFactory;
    readonly ILoggerFactory loggerFactory;
    readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcDurableTaskWorker" /> class.
    /// </summary>
    /// <param name="name">The name of the worker.</param>
    /// <param name="factory">The task factory.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
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
        ILoggerFactory loggerFactory,
        IHttpClientFactory? httpClientFactory = null)
        : base(name, factory)
    {
        this.grpcOptions = Check.NotNull(grpcOptions).Get(name);
        this.workerOptions = Check.NotNull(workerOptions).Get(name);
        this.services = Check.NotNull(services);
        this.httpClientFactory = httpClientFactory;
        this.loggerFactory = Check.NotNull(loggerFactory);
        this.logger = loggerFactory.CreateLogger("Dapr.DurableTask");
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using AsyncDisposable disposable = this.GetCallInvoker(out CallInvoker callInvoker, out string address);
        this.logger.StartingTaskHubWorker(address);
        await new Processor(this, new(callInvoker)).ExecuteAsync(stoppingToken);
    }

    GrpcChannel GetChannel(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            address = "http://localhost:4001";
        }

        // Create the HttpClient so we can avoid the default 100 second timeout
        // As this service is created as a singleton, it's ok to create the HttpClient once here as well
        // Create the client from IHttpClientFactory if available, otherwise create a new instance
        var httpClient = this.httpClientFactory?.CreateClient() ?? new HttpClient();
        httpClient.Timeout = Timeout.InfiniteTimeSpan;

        // Configure keep-alive settings to maintain long-lived connections
        var handler = new SocketsHttpHandler
        {
            // Enable keep-alive
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(30),

            // Pooled connections are reused and won't time out from inactivity
            EnableMultipleHttp2Connections = true,

            // Set a very long connection lifetime - this allows a controlled connection refresh strategy
            PooledConnectionLifetime = TimeSpan.FromDays(1),

            // Disable idle timeout entirely
            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
        };

        return GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            HttpHandler = handler,
            HttpClient = httpClient,
            MaxReceiveMessageSize = null, // No message size limit
            DisposeHttpClient = false, // Lifetime managed by the HttpClientFactory
            ServiceConfig = new ServiceConfig
            {
                MethodConfigs =
                {
                    new MethodConfig
                    {
                        Names = { MethodName.Default },
                        RetryPolicy = new global::Grpc.Net.Client.Configuration.RetryPolicy
                        {
                            MaxAttempts = 5,
                            MaxBackoff = TimeSpan.FromSeconds(12),
                            BackoffMultiplier = 1.25,
                            InitialBackoff = TimeSpan.FromSeconds(2),
                        },
                    },
                },
            },
        });
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

        c = this.GetChannel(this.grpcOptions.Address);
        callInvoker = c.CreateCallInvoker();
        address = c.Target;
        return new AsyncDisposable(() => new(c.ShutdownAsync()));
    }
}
