// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Grpc;
using Microsoft.DurableTask.Testing.Sidecar;
using Microsoft.DurableTask.Testing.Sidecar.Grpc;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Testing;

/// <summary>
/// Extension methods for integrating in-memory durable task testing with your existing DI container,
/// such as WebApplicationFactory.
/// </summary>
public static class DurableTaskTestExtensions
{
    /// <summary>
    /// These extensions allow you to inject the <see cref="InMemoryOrchestrationService"/> into your
    /// existing test host so that your orchestrations and activities can resolve services from your DI container.
    /// </summary>
    /// <param name="services">The service collection (from your WebApplicationFactory or host).</param>
    /// <param name="configureTasks">Action to register orchestrators and activities.</param>
    /// <param name="options">Optional configuration options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddInMemoryDurableTask(
        this IServiceCollection services,
        Action<DurableTaskRegistry> configureTasks,
        InMemoryDurableTaskOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureTasks);

        options ??= new InMemoryDurableTaskOptions();

        // Determine port for the internal gRPC server
        int port = options.Port ?? Random.Shared.Next(30000, 40000);
        string address = $"http://localhost:{port}";

        // Register the in-memory orchestration service as a singleton
        services.AddSingleton<InMemoryOrchestrationService>(sp =>
        {
            var loggerFactory = sp.GetService<ILoggerFactory>();
            return new InMemoryOrchestrationService(loggerFactory);
        });
        services.AddSingleton<IOrchestrationService>(sp => sp.GetRequiredService<InMemoryOrchestrationService>());
        services.AddSingleton<IOrchestrationServiceClient>(sp => sp.GetRequiredService<InMemoryOrchestrationService>());

        // Register the gRPC sidecar server as a hosted service
        services.AddSingleton<TaskHubGrpcServer>();
        services.AddHostedService<InMemoryGrpcSidecarHost>(sp =>
        {
            return new InMemoryGrpcSidecarHost(
                address,
                sp.GetRequiredService<InMemoryOrchestrationService>(),
                sp.GetService<ILoggerFactory>());
        });

        // Create a gRPC channel that will connect to our internal sidecar
        services.AddSingleton<GrpcChannel>(sp => GrpcChannel.ForAddress(address));

        // Register the durable task worker (connects to our internal sidecar)
        services.AddDurableTaskWorker(builder =>
        {
            builder.UseGrpc(address);
            builder.AddTasks(configureTasks);
        });

        // Register the durable task client (connects to our internal sidecar)
        services.AddDurableTaskClient(builder =>
        {
            builder.UseGrpc(address);
            builder.RegisterDirectly();
        });

        return services;
    }

    /// <summary>
    /// Gets the <see cref="InMemoryOrchestrationService"/> from the service provider.
    /// Useful for advanced scenarios like inspecting orchestration state.
    /// </summary>
    /// <param name="services">The service provider.</param>
    /// <returns>The in-memory orchestration service instance.</returns>
    public static InMemoryOrchestrationService GetInMemoryOrchestrationService(this IServiceProvider services)
    {
        return services.GetRequiredService<InMemoryOrchestrationService>();
    }
}

/// <summary>
/// Options for configuring in-memory durable task support.
/// </summary>
public class InMemoryDurableTaskOptions
{
    /// <summary>
    /// Gets or sets the port for the internal gRPC server.
    /// If not set, a random port between 30000-40000 will be used.
    /// </summary>
    public int? Port { get; set; }
}

/// <summary>
/// Internal hosted service that runs the gRPC sidecar within the user's host.
/// </summary>
sealed class InMemoryGrpcSidecarHost : IHostedService, IAsyncDisposable
{
    readonly string address;
    readonly InMemoryOrchestrationService orchestrationService;
    readonly ILoggerFactory? loggerFactory;
    IHost? inMemorySidecarHost;

    public InMemoryGrpcSidecarHost(
        string address,
        InMemoryOrchestrationService orchestrationService,
        ILoggerFactory? loggerFactory)
    {
        this.address = address;
        this.orchestrationService = orchestrationService;
        this.loggerFactory = loggerFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Build and start the gRPC sidecar
        this.inMemorySidecarHost = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                if (this.loggerFactory != null)
                {
                    logging.Services.AddSingleton(this.loggerFactory);
                }
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseUrls(this.address);
                webBuilder.ConfigureKestrel(kestrelOptions =>
                {
                    kestrelOptions.ConfigureEndpointDefaults(listenOptions =>
                        listenOptions.Protocols = HttpProtocols.Http2);
                });

                webBuilder.ConfigureServices(services =>
                {
                    services.AddGrpc();
                    // Use the SAME orchestration service instance
                    services.AddSingleton<IOrchestrationService>(this.orchestrationService);
                    services.AddSingleton<IOrchestrationServiceClient>(this.orchestrationService);
                    services.AddSingleton<TaskHubGrpcServer>();
                });

                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGrpcService<TaskHubGrpcServer>();
                    });
                });
            })
            .Build();

        await this.inMemorySidecarHost.StartAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (this.inMemorySidecarHost != null)
        {
            await this.inMemorySidecarHost.StopAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (this.inMemorySidecarHost != null)
        {
            await this.inMemorySidecarHost.StopAsync();
            this.inMemorySidecarHost.Dispose();
        }
    }
}
