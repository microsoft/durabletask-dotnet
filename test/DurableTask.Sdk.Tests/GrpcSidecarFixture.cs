// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.DurableTask.Grpc;
using Microsoft.DurableTask.Sidecar;
using Microsoft.DurableTask.Sidecar.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Tests;

public sealed class GrpcSidecarFixture : IDisposable
{
    // Use a random port number to allow multiple instances to run in parallel
    const string ListenHost = "127.0.0.1";
    readonly int ListenPort = Random.Shared.Next(30000, 40000);

    readonly IWebHost host;
    readonly Channel sidecarChannel;

    public GrpcSidecarFixture()
    {
        InMemoryOrchestrationService service = new();

        this.host = new WebHostBuilder()
            .UseKestrel(options =>
            {
                // Need to force Http2 in Kestrel in unencrypted scenarios
                // https://docs.microsoft.com/en-us/aspnet/core/grpc/troubleshoot?view=aspnetcore-3.0
                options.ConfigureEndpointDefaults(listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
            })
            .UseUrls($"http://{ListenHost}:{this.ListenPort}")
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
            })
            .Build();

        this.host.Start();

        this.sidecarChannel = new Channel(ListenHost, this.ListenPort, ChannelCredentials.Insecure);
    }

    public DurableTaskGrpcWorker.Builder GetWorkerBuilder()
    {
        // The gRPC channel is reused across tests to avoid the overhead of creating new connections (which is very slow)
        return DurableTaskGrpcWorker.CreateBuilder().UseGrpcChannel(this.sidecarChannel);
    }

    public DurableTaskGrpcClient.Builder GetClientBuilder()
    {
        // The gRPC channel is reused across tests to avoid the overhead of creating new connections (which is very slow)
        return DurableTaskGrpcClient.CreateBuilder().UseGrpcChannel(this.sidecarChannel);
    }

    public void Dispose()
    {
        this.sidecarChannel.ShutdownAsync().GetAwaiter().GetResult();

        this.host.StopAsync().GetAwaiter().GetResult();
        this.host.Dispose();
    }
}
