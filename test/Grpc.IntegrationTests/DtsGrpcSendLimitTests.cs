// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Client.Grpc;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Grpc.Tests;

/// <summary>
/// Verifies outgoing message size limits on Durable Task Scheduler channels.
/// </summary>
public class DtsGrpcSendLimitTests
{
    const int DefaultLimit = 4 * 1024 * 1024;
    const int RaisedLimit = 5 * 1024 * 1024;

    /// <summary>
    /// Verifies the exact size boundary and applies an override to the same named scheduler.
    /// </summary>
    [Theory]
    [InlineData(false, RaisedLimit)]
    [InlineData(true, RaisedLimit)]
    [InlineData(false, null)]
    [InlineData(true, null)]
    public async Task DtsChannel_EnforcesDefaultAndHonorsOverrideAsync(bool useWorker, int? overrideLimit)
    {
        // Arrange
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        using IHost host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureWebHostDefaults(web => web
                .UseKestrel(options => options.Listen(IPAddress.Loopback, 0, endpoint => endpoint.Protocols = HttpProtocols.Http2))
                .ConfigureServices(services =>
                {
                    services.AddGrpc(options => options.MaxReceiveMessageSize = RaisedLimit + 1);
                    services.AddSingleton<CountingSidecar>();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapGrpcService<CountingSidecar>());
                }))
            .Build();
        await host.StartAsync(timeout.Token);
        CountingSidecar sidecar = host.Services.GetRequiredService<CountingSidecar>();
        string endpoint = Assert.Single(host.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses);

        ServiceCollection services = new();
        bool useOverride = false;
        RegisterScheduler(services, useWorker, endpoint, currentLimit => useOverride ? overrideLimit : currentLimit);
        await using ServiceProvider provider = services.BuildServiceProvider();
        GrpcChannel initialChannel = GetChannel(provider, useWorker);
        P.TaskHubSidecarService.TaskHubSidecarServiceClient initialClient = new(initialChannel);

        // Act and assert
        await initialClient.RaiseEventAsync(CreateRequest(DefaultLimit), cancellationToken: timeout.Token);
        RpcException error = await Assert.ThrowsAsync<RpcException>(
            async () => await initialClient.RaiseEventAsync(CreateRequest(DefaultLimit + 1), cancellationToken: timeout.Token));
        Assert.Equal(StatusCode.ResourceExhausted, error.StatusCode);
        Assert.Equal(1, sidecar.Received);

        // Rebuild the scheduler options for the same name so only the send limit changes the channel cache key.
        useOverride = true;
        Assert.True(useWorker
            ? provider.GetRequiredService<IOptionsMonitorCache<DurableTaskSchedulerWorkerOptions>>().TryRemove(Options.DefaultName)
            : provider.GetRequiredService<IOptionsMonitorCache<DurableTaskSchedulerClientOptions>>().TryRemove(Options.DefaultName));

        GrpcChannel updatedChannel = GetChannel(provider, useWorker);
        Assert.NotSame(initialChannel, updatedChannel);
        Assert.Same(updatedChannel, GetChannel(provider, useWorker));
        P.TaskHubSidecarService.TaskHubSidecarServiceClient updatedClient = new(updatedChannel);
        await updatedClient.RaiseEventAsync(CreateRequest(RaisedLimit), cancellationToken: timeout.Token);
        if (overrideLimit.HasValue)
        {
            error = await Assert.ThrowsAsync<RpcException>(
                async () => await updatedClient.RaiseEventAsync(CreateRequest(RaisedLimit + 1), cancellationToken: timeout.Token));
            Assert.Equal(StatusCode.ResourceExhausted, error.StatusCode);
            Assert.Equal(2, sidecar.Received);
        }
        else
        {
            await updatedClient.RaiseEventAsync(CreateRequest(RaisedLimit + 1), cancellationToken: timeout.Token);
            Assert.Equal(3, sidecar.Received);
        }

        await host.StopAsync(timeout.Token);
    }

    /// <summary>
    /// Verifies that options validation rejects invalid limits.
    /// </summary>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, -1)]
    [InlineData(true, 0)]
    [InlineData(true, -1)]
    public async Task DtsChannel_RejectsNonpositiveLimitAsync(bool useWorker, int limit)
    {
        // Arrange
        ServiceCollection services = new();
        RegisterScheduler(services, useWorker, "http://localhost", _ => limit);
        await using ServiceProvider provider = services.BuildServiceProvider();

        // Act and assert
        OptionsValidationException error = Assert.Throws<OptionsValidationException>(() => GetChannel(provider, useWorker));
        Assert.Contains("MaxSendMessageSize", error.Message);
    }

    static void RegisterScheduler(ServiceCollection services, bool useWorker, string endpoint, Func<int?, int?> configureLimit)
    {
        string connectionString = $"Endpoint={endpoint};TaskHub=test;Authentication=None";
        if (useWorker)
        {
            services.AddDurableTaskWorker(builder => builder.UseDurableTaskScheduler(connectionString, options =>
            {
                options.WorkerId = "test";
                options.MaxSendMessageSize = configureLimit(options.MaxSendMessageSize);
            }));
        }
        else
        {
            services.AddDurableTaskClient(builder => builder.UseDurableTaskScheduler(connectionString,
                options => options.MaxSendMessageSize = configureLimit(options.MaxSendMessageSize)));
        }
    }

    static GrpcChannel GetChannel(IServiceProvider provider, bool useWorker) => useWorker
        ? provider.GetRequiredService<IOptionsFactory<GrpcDurableTaskWorkerOptions>>().Create(Options.DefaultName).Channel!
        : provider.GetRequiredService<IOptionsFactory<GrpcDurableTaskClientOptions>>().Create(Options.DefaultName).Channel!;

    static P.RaiseEventRequest CreateRequest(int size)
    {
        P.RaiseEventRequest request = new() { InstanceId = "test", Name = "event", Input = new string('x', size) };
        int overhead = request.CalculateSize() - size;
        request.Input = request.Input[..(size - overhead)];
        Assert.Equal(size, request.CalculateSize());
        return request;
    }

    sealed class CountingSidecar : P.TaskHubSidecarService.TaskHubSidecarServiceBase
    {
        int received;

        public int Received => Volatile.Read(ref this.received);

        /// <inheritdoc />
        public override Task<P.RaiseEventResponse> RaiseEvent(P.RaiseEventRequest request, ServerCallContext context)
        {
            Interlocked.Increment(ref this.received);
            return Task.FromResult(new P.RaiseEventResponse());
        }
    }
}
