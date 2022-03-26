// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using DurableTask.Core;
using DurableTask.Grpc;
using DurableTask.Sidecar;
using DurableTask.Sidecar.Grpc;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DurableTask.Sdk.Tests;

public sealed class GrpcSidecarFixture : IDisposable
{
    // Use a random port number to allow multiple instances to run in parallel
    readonly string listenAddress = $"http://127.0.0.1:{Random.Shared.Next(30000, 40000)}";

    readonly IWebHost host;
    readonly GrpcChannel sidecarChannel;

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
            .UseUrls(this.listenAddress)
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

        this.sidecarChannel = GrpcChannel.ForAddress(this.listenAddress);
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
        this.sidecarChannel.Dispose();

        this.host.StopAsync().GetAwaiter().GetResult();
        this.host.Dispose();
    }
}
