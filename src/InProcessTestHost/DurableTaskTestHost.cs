// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Sidecar;
using Microsoft.DurableTask.Sidecar.Grpc;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Testing;

/// <summary>
/// In-process test host for testing class-based durable task orchestrations and activities
/// without requiring any external backend (Azure Storage, SQL, etc).
/// </summary>
public sealed class DurableTaskTestHost : IAsyncDisposable
{
    readonly IWebHost sidecarHost;
    readonly IHost workerHost;
    readonly GrpcChannel grpcChannel;

    /// <summary>
    /// Initializes a new instance of the <see cref="DurableTaskTestHost"/> class.
    /// </summary>
    /// <param name="sidecarHost">The gRPC sidecar host.</param>
    /// <param name="workerHost">The worker host.</param>
    /// <param name="grpcChannel">The gRPC channel.</param>
    /// <param name="client">The durable task client.</param>
    public DurableTaskTestHost(IWebHost sidecarHost, IHost workerHost, GrpcChannel grpcChannel, DurableTaskClient client)
    {
        this.sidecarHost = sidecarHost;
        this.workerHost = workerHost;
        this.grpcChannel = grpcChannel;
        this.Client = client;
    }

    /// <summary>
    /// Gets the durable task client for scheduling and managing orchestrations.
    /// </summary>
    public DurableTaskClient Client { get; }

    /// <summary>
    /// Starts a new in-process test host with the specified orchestrators and activities.
    /// </summary>
    /// <param name="registry">Action to configure the task registry by adding orchestrators and activities.</param>
    /// <param name="options">Optional configuration options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A running test host ready to execute orchestrations.</returns>
    public static async Task<DurableTaskTestHost> StartAsync(
        Action<DurableTaskRegistry> registry,
        DurableTaskTestHostOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DurableTaskTestHostOptions();

        // Create in-memory orchestration service
        var orchestrationService = new InMemoryOrchestrationService(options.LoggerFactory);

        // Start gRPC sidecar server in-process
        string address = options.Port.HasValue
            ? $"http://localhost:{options.Port.Value}"
            : $"http://localhost:{Random.Shared.Next(30000, 40000)}";

        var sidecarHost = new WebHostBuilder()
            .UseKestrel(kestrelOptions =>
            {
                // Configure for HTTP/2 (required for gRPC)
                kestrelOptions.ConfigureEndpointDefaults(listenOptions =>
                    listenOptions.Protocols = HttpProtocols.Http2);
            })
            .UseUrls(address)
            .ConfigureServices(services =>
            {
                services.AddGrpc();
                services.AddSingleton<IOrchestrationService>(orchestrationService);
                services.AddSingleton<IOrchestrationServiceClient>(orchestrationService);
                services.AddSingleton<TaskHubGrpcServer>();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGrpcService<TaskHubGrpcServer>();
                });
            })
            .Build();

        sidecarHost.Start();
        var grpcChannel = GrpcChannel.ForAddress(address);

        // Create worker host with user's orchestrators and activities
        var workerHost = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                if (options.LoggerFactory != null)
                {
                    logging.Services.AddSingleton(options.LoggerFactory);
                }
            })
            .ConfigureServices(services =>
            {
                // Register worker that connects to our in-process sidecar
                services.AddDurableTaskWorker(builder =>
                {
                    builder.UseGrpc(grpcChannel);
                    builder.AddTasks(registry);
                });

                // Register client that connects to the same sidecar
                services.AddDurableTaskClient(builder =>
                {
                    builder.UseGrpc(grpcChannel);
                    builder.RegisterDirectly();
                });
            })
            .Build();

        await workerHost.StartAsync(cancellationToken);

        // Get the client from the worker host
        var client = workerHost.Services.GetRequiredService<DurableTaskClient>();

        return new DurableTaskTestHost(sidecarHost, workerHost, grpcChannel, client);
    }

    /// <summary>
    /// Clean up all resources.
    /// </summary>
    /// <returns>A task representing the asynchronous dispose operation.</returns>
    public async ValueTask DisposeAsync()
    {
        await this.workerHost.StopAsync();
        this.workerHost.Dispose();

        await this.grpcChannel.ShutdownAsync();
        this.grpcChannel.Dispose();

        await this.sidecarHost.StopAsync();
        this.sidecarHost.Dispose();
    }
}

/// <summary>
/// Configuration options for <see cref="DurableTaskTestHost"/>.
/// </summary>
public class DurableTaskTestHostOptions
{
    /// <summary>
    /// Gets or sets the specific port to use for the gRPC sidecar.
    /// If not set, a random port between 30000-40000 will be used.
    /// </summary>
    public int? Port { get; set; }

    /// <summary>
    /// Gets or sets an optional logger factory for capturing logs during tests.
    /// Null by default.
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; set; }
}

