// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit.Abstractions;

namespace Microsoft.DurableTask.Grpc.Tests;

/// <summary>
/// Uses loopback HTTP/2 only, with real worker-owned channels and registered purge activity factories.
/// </summary>
public class NamedPurgeTransportTests(ITestOutputHelper output)
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [Theory]
    [InlineData("A", "B")]
    [InlineData("B", "A")]
    [InlineData("", "named")]
    public async Task StoppingLastAddressOnlyWorker_PreservesOtherPurgeActivitiesAsync(string first, string last)
    {
        // Arrange
        await using LoopbackSidecar sidecar = await LoopbackSidecar.StartAsync();
        ServiceCollection services = CreateServices();
        RegisterWorker(services, first, sidecar.Address);
        RegisterWorker(services, last, sidecar.Address);
        await using ServiceProvider provider = services.BuildServiceProvider();
        IHostedService[] workers = provider.GetServices<IHostedService>().ToArray();
        var firstActivities = GetActivities(provider, first);
        var lastActivities = GetActivities(provider, last);

        // Act
        try
        {
            await workers[0].StartAsync(default);
            ObservedCall firstStream = await sidecar.NextStreamAsync();
            await RunBothAsync(firstActivities);
            await workers[1].StartAsync(default);
            ObservedCall lastStream = await sidecar.NextStreamAsync();
            await RunBothAsync(lastActivities);
            await workers[1].StopAsync(default).WaitAsync(Timeout);
            await sidecar.StreamClosedAsync(lastStream.Connection);
            Exception? stoppedError = await Record.ExceptionAsync(() => lastActivities.Get.RunAsync(null!, 1));
            Exception? getError = await Record.ExceptionAsync(() => firstActivities.Get.RunAsync(null!, 1));
            Exception? reportError = await Record.ExceptionAsync(() => RunReportAsync(firstActivities.Report));

            // Assert
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                First = first, Last = last, FirstStream = firstStream.Connection, LastStream = lastStream.Connection,
                FirstStillConnected = sidecar.IsStreamActive(firstStream.Connection),
                StoppedWorkerError = stoppedError?.GetType().Name,
                SurvivingGetError = getError?.GetType().Name,
                SurvivingReportError = reportError?.GetType().Name,
                Calls = sidecar.Calls,
            }));
            Assert.IsType<ObjectDisposedException>(stoppedError);
            Assert.True(sidecar.IsStreamActive(firstStream.Connection));
            Assert.Null(getError);
            Assert.Null(reportError);
            Assert.All(sidecar.PurgeCalls.TakeLast(2), call =>
            {
                Assert.Equal((Marker(first), firstStream.Connection), (call.Worker, call.Connection));
                Assert.Equal("synthetic-auth", call.Authorization);
            });
            Assert.Single(sidecar.Calls, call => call.Worker == Marker(first) && call.Method == "Hello");
            Assert.Single(sidecar.Calls, call => call.Worker == Marker(first) && call.Method == "GetWorkItems");
        }
        finally
        {
            await StopAsync(workers);
        }
    }

    [Theory]
    [InlineData("A", "B")]
    [InlineData("", "named")]
    public async Task ActiveWorkers_UseTheirOwnRegistryTransportAsync(string first, string last)
    {
        // Arrange
        await using LoopbackSidecar sidecar = await LoopbackSidecar.StartAsync();
        ServiceCollection services = CreateServices();
        RegisterWorker(services, first, sidecar.Address);
        RegisterWorker(services, last, sidecar.Address);
        await using ServiceProvider provider = services.BuildServiceProvider();
        IHostedService[] workers = provider.GetServices<IHostedService>().ToArray();

        // Act
        try
        {
            await workers[0].StartAsync(default);
            ObservedCall firstStream = await sidecar.NextStreamAsync();
            await workers[1].StartAsync(default);
            ObservedCall lastStream = await sidecar.NextStreamAsync();
            await RunBothAsync(GetActivities(provider, first));
            ObservedCall[] firstCalls = sidecar.PurgeCalls;
            await RunBothAsync(GetActivities(provider, last));
            ObservedCall[] lastCalls = sidecar.PurgeCalls.Skip(2).ToArray();

            // Assert
            Assert.NotEqual(firstStream.Connection, lastStream.Connection);
            Assert.Equal(2, firstCalls.Length);
            Assert.Equal(2, lastCalls.Length);
            Assert.All(firstCalls, call => Assert.Equal((Marker(first), firstStream.Connection), (call.Worker, call.Connection)));
            Assert.All(lastCalls, call => Assert.Equal((Marker(last), lastStream.Connection), (call.Worker, call.Connection)));
            Assert.All(sidecar.Calls, call => Assert.Equal("synthetic-auth", call.Authorization));
        }
        finally
        {
            await StopAsync(workers);
        }
    }

    [Fact]
    public async Task ServiceProvidersBuiltFromOneCollection_DoNotShareTransportStateAsync()
    {
        // Arrange
        await using LoopbackSidecar sidecar = await LoopbackSidecar.StartAsync();
        ServiceCollection services = CreateServices();
        RegisterWorker(services, string.Empty, sidecar.Address);
        await using ServiceProvider first = services.BuildServiceProvider();
        await using ServiceProvider second = services.BuildServiceProvider();
        IHostedService[] workers = [Assert.Single(first.GetServices<IHostedService>()), Assert.Single(second.GetServices<IHostedService>())];
        var activities = GetActivities(first, string.Empty);

        // Act
        try
        {
            await workers[0].StartAsync(default);
            ObservedCall firstStream = await sidecar.NextStreamAsync();
            await workers[1].StartAsync(default);
            ObservedCall lastStream = await sidecar.NextStreamAsync();
            await RunBothAsync(GetActivities(second, string.Empty));
            await workers[1].StopAsync(default).WaitAsync(Timeout);
            await sidecar.StreamClosedAsync(lastStream.Connection);
            await RunBothAsync(activities);

            // Assert
            Assert.NotEqual(firstStream.Connection, lastStream.Connection);
            Assert.True(sidecar.IsStreamActive(firstStream.Connection));
            Assert.All(sidecar.PurgeCalls.TakeLast(2), call => Assert.Equal(firstStream.Connection, call.Connection));
        }
        finally
        {
            await StopAsync(workers);
        }
    }

    [Fact]
    public async Task UnstartedWorker_ReportsMissingOwnTransportEvenWhenAnotherWorkerIsRunningAsync()
    {
        // Arrange
        await using LoopbackSidecar sidecar = await LoopbackSidecar.StartAsync();
        ServiceCollection services = CreateServices();
        RegisterWorker(services, "running", sidecar.Address);
        RegisterWorker(services, "unstarted", sidecar.Address);
        await using ServiceProvider provider = services.BuildServiceProvider();
        IHostedService[] workers = provider.GetServices<IHostedService>().ToArray();
        var unstarted = GetActivities(provider, "unstarted");

        // Act
        try
        {
            await workers[0].StartAsync(default);
            await sidecar.NextStreamAsync();
            Exception? getError = await Record.ExceptionAsync(() => unstarted.Get.RunAsync(null!, 1));
            Exception? reportError = await Record.ExceptionAsync(() => RunReportAsync(unstarted.Report));

            // Assert
            Assert.IsType<InvalidOperationException>(getError);
            Assert.IsType<InvalidOperationException>(reportError);
            Assert.Empty(sidecar.PurgeCalls);
        }
        finally
        {
            await StopAsync(workers);
        }
    }

    static ServiceCollection CreateServices()
    {
        ServiceCollection services = new();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<PayloadStore>(Mock.Of<PayloadStore>());
        return services;
    }

    static void RegisterWorker(ServiceCollection services, string name, string address)
        => services.AddDurableTaskWorker(name, builder =>
        {
            builder.UseGrpc(options =>
            {
                options.Address = address;
                options.Interceptors.Add(new IdentityInterceptor(Marker(name)));
            });
            builder.UseExternalizedPayloads();
        });

    static (GetLargePayloadTombstonesActivity Get, ReportLargePayloadPurgeResultsActivity Report) GetActivities(
        IServiceProvider provider, string name)
    {
        DurableTaskRegistry registry = provider.GetRequiredService<IOptionsMonitor<DurableTaskRegistry>>().Get(name);
        return (Create<GetLargePayloadTombstonesActivity>(), Create<ReportLargePayloadPurgeResultsActivity>());

        T Create<T>() where T : class, ITaskActivity
            => Assert.IsType<T>(Assert.Single(registry.GetActivities(), pair => pair.Key.Name == typeof(T).Name).Value(provider));
    }

    static async Task RunBothAsync((GetLargePayloadTombstonesActivity Get, ReportLargePayloadPurgeResultsActivity Report) activities)
    {
        await activities.Get.RunAsync(null!, 1).WaitAsync(Timeout);
        await RunReportAsync(activities.Report).WaitAsync(Timeout);
    }

    static Task RunReportAsync(ReportLargePayloadPurgeResultsActivity activity)
        => activity.RunAsync(null!, [new LargePayloadPurgeResult("opaque-tombstone", LargePayloadPurgeDisposition.Deleted)]);

    static Task StopAsync(IHostedService[] workers)
        => Task.WhenAll(workers.Select(worker => worker.StopAsync(default))).WaitAsync(Timeout);

    static string Marker(string name) => string.IsNullOrEmpty(name) ? "default" : name;

    sealed class IdentityInterceptor(string worker) : Interceptor
    {
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            TRequest request, ClientInterceptorContext<TRequest, TResponse> context, AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
            => continuation(request, this.WithHeaders(context));

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            TRequest request, ClientInterceptorContext<TRequest, TResponse> context, AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
            => continuation(request, this.WithHeaders(context));

        ClientInterceptorContext<TRequest, TResponse> WithHeaders<TRequest, TResponse>(ClientInterceptorContext<TRequest, TResponse> context)
            where TRequest : class
            where TResponse : class
            => new(context.Method, context.Host, context.Options.WithHeaders(
                new Metadata { { "x-test-worker", worker }, { "authorization", "synthetic-auth" } }));
    }

    sealed record ObservedCall(string Worker, string Method, string Connection, string Authorization);

    sealed class LoopbackSidecar : IAsyncDisposable
    {
        readonly Channel<ObservedCall> streams = Channel.CreateUnbounded<ObservedCall>();
        readonly ConcurrentQueue<ObservedCall> calls = new();
        readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> closed = new();
        IHost host = null!;

        public string Address { get; private set; } = string.Empty;

        public ObservedCall[] Calls => this.calls.ToArray();

        public ObservedCall[] PurgeCalls => this.Calls.Where(call => call.Method is "GetLargePayloadTombstones" or "ReportLargePayloadPurgeResults").ToArray();

        public static async Task<LoopbackSidecar> StartAsync()
        {
            LoopbackSidecar sidecar = new();
            sidecar.host = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging => logging.ClearProviders())
                .ConfigureWebHostDefaults(web => web
                    .UseKestrel(options => options.Listen(IPAddress.Loopback, 0, endpoint => endpoint.Protocols = HttpProtocols.Http2))
                    .Configure(app => app.Run(sidecar.HandleAsync)))
                .Build();
            await sidecar.host.StartAsync().WaitAsync(Timeout);
            sidecar.Address = Assert.Single(sidecar.host.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses);
            return sidecar;
        }

        public Task<ObservedCall> NextStreamAsync() => this.streams.Reader.ReadAsync().AsTask().WaitAsync(Timeout);

        public Task StreamClosedAsync(string connection) => this.closed[connection].Task.WaitAsync(Timeout);

        public bool IsStreamActive(string connection) => !this.closed[connection].Task.IsCompleted;

        public async ValueTask DisposeAsync()
        {
            using IHost host = this.host;
            await host.StopAsync().WaitAsync(Timeout);
        }

        async Task HandleAsync(HttpContext context)
        {
            string method = context.Request.Path.Value!.Split('/').Last();
            Assert.Contains(method, new[] { "Hello", "GetWorkItems", "GetLargePayloadTombstones", "ReportLargePayloadPurgeResults" });
            await context.Request.Body.CopyToAsync(Stream.Null, context.RequestAborted);
            ObservedCall call = new(context.Request.Headers["x-test-worker"].ToString(), method,
                context.Connection.Id, context.Request.Headers.Authorization.ToString());
            this.calls.Enqueue(call);
            context.Response.ContentType = "application/grpc";
            context.Response.DeclareTrailer("grpc-status");
            if (method == "GetWorkItems")
            {
                TaskCompletionSource<bool> stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Assert.True(this.closed.TryAdd(call.Connection, stopped));
                await context.Response.StartAsync(context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
                await this.streams.Writer.WriteAsync(call, context.RequestAborted);
                try
                {
                    await Task.Delay(System.Threading.Timeout.Infinite, context.RequestAborted);
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                {
                    // The client disconnected or aborted this parked work-item request.
                }
                finally
                {
                    stopped.TrySetResult(true);
                }

                return;
            }

            await context.Response.Body.WriteAsync(new byte[5], context.RequestAborted);
            context.Response.AppendTrailer("grpc-status", "0");
        }
    }
}
