// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.DurableTask.Testing.Sidecar;
using Microsoft.DurableTask.Testing.Sidecar.Grpc;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DurableTask.Grpc.Tests;

public sealed class GrpcSidecarFixture : IDisposable
{
    const string ListenHost = "localhost";

    readonly IHost host;

    public GrpcSidecarFixture()
    {
        InMemoryOrchestrationService service = new();

        // Use a random port number to allow multiple instances to run in parallel
        string address = $"http://{ListenHost}:{Random.Shared.Next(30000, 40000)}";
        this.host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                // Reduce logging verbosity to make test output more readable and organized.
                // Filter out noisy ASP.NET Core and gRPC infrastructure logs while keeping
                // important DurableTask sidecar logs for debugging.
                logging.SetMinimumLevel(LogLevel.Warning);
                logging.AddFilter("Microsoft.DurableTask.Sidecar", LogLevel.Information);
                logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder
                    .UseKestrel(options =>
                    {
                        // Need to force Http2 in Kestrel in unencrypted scenarios
                        // https://docs.microsoft.com/en-us/aspnet/core/grpc/troubleshoot?view=aspnetcore-3.0
                        options.ConfigureEndpointDefaults(listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
                    })
                    .UseUrls(address)
                    .ConfigureServices(services =>
                    {
                        services.AddGrpc();
                        services.AddSingleton<IOrchestrationService>(service);
                        services.AddSingleton<IOrchestrationServiceClient>(service);
                        services.AddSingleton<TaskHubGrpcServer>();
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGrpcService<TaskHubGrpcServer>();
                        });
                    });
            })
            .Build();

        this.host.Start();

        this.Channel = GrpcChannel.ForAddress(address);
    }

    public GrpcChannel Channel { get; }

    public void Dispose()
    {
        this.Channel.ShutdownAsync().GetAwaiter().GetResult();
        this.host.StopAsync().GetAwaiter().GetResult();
        this.host.Dispose();
    }
}
