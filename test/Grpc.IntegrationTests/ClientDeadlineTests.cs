// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Threading.Channels;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Client.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit.Abstractions;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Grpc.Tests;

public class ClientDeadlineTests(ITestOutputHelper output)
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    static readonly TimeSpan CallTimeout = TimeSpan.FromMilliseconds(500);

    [Fact]
    public async Task StalledReads_WithInterleavedCancellation_RecreateOnceAndRecoverSharedChannelAsync()
    {
        // Arrange
        await using StalledSidecar sidecar = await StalledSidecar.StartAsync();
        ServiceCollection services = new();
        services.AddLogging();
        Mock<PayloadStore> payloadStore = new(MockBehavior.Strict);
        payloadStore.Setup(store => store.IsKnownPayloadToken(It.IsAny<string>()))
            .Returns((string value) => value == "test-payload");
        payloadStore.Setup(store => store.DownloadAsync("test-payload", It.IsAny<CancellationToken>()))
            .ReturnsAsync("\"restored\"");
        services.AddSingleton(payloadStore.Object);
        services.AddDurableTaskClient("deadline", builder =>
        {
            builder.UseDurableTaskScheduler(
                $"Endpoint={sidecar.Address};TaskHub=test;Authentication=None",
                options => options.DefaultUnaryCallTimeout = CallTimeout);
            builder.UseExternalizedPayloads();
        });
        await using ServiceProvider provider = services.BuildServiceProvider();
        IOptionsFactory<GrpcDurableTaskClientOptions> factory =
            provider.GetRequiredService<IOptionsFactory<GrpcDurableTaskClientOptions>>();
        GrpcDurableTaskClientOptions firstOptions = factory.Create("deadline");
        GrpcDurableTaskClientOptions peerOptions = factory.Create("deadline");
        using GrpcChannel initialChannel = firstOptions.Channel!;
        Assert.Same(initialChannel, peerOptions.Channel);
        RecreationLogger firstLogger = new();
        RecreationLogger peerLogger = new();
        await using DurableTaskClient first = new GrpcDurableTaskClient("deadline", firstOptions, firstLogger);
        await using DurableTaskClient peer = new GrpcDurableTaskClient("deadline", peerOptions, peerLogger);

        // Act
        for (int i = 0; i < 4; i++)
        {
            await this.AssertDeadlineAsync(first, sidecar);
        }

        using (CancellationTokenSource cancellation = new())
        {
            Task<OrchestrationMetadata?> cancelled = first.GetInstanceAsync("cancel", cancellation.Token);
            await sidecar.NextCallAsync();
            cancellation.Cancel();
            RpcException error = await Assert.ThrowsAsync<RpcException>(() => cancelled.WaitAsync(Timeout));
            Assert.Equal(StatusCode.Cancelled, error.StatusCode);
        }

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => this.AssertDeadlineAsync(first, sidecar)));
        await firstLogger.Recreated.Task.WaitAsync(Timeout);
        GrpcDurableTaskClientOptions refreshedOptions = factory.Create("deadline");
        OrchestrationMetadata? recovered = await first.GetInstanceAsync("recovered", getInputsAndOutputs: true).WaitAsync(Timeout);
        ObservedCall recoveredCall = await sidecar.NextCallAsync();

        for (int i = 0; i < 5; i++)
        {
            await this.AssertDeadlineAsync(peer, sidecar);
        }

        await peerLogger.Recreated.Task.WaitAsync(Timeout);
        OrchestrationMetadata? peerRecovered = await peer.GetInstanceAsync("recovered", getInputsAndOutputs: true).WaitAsync(Timeout);
        ObservedCall peerCall = await sidecar.NextCallAsync();

        // Assert
        Assert.Equal("healthy", recovered?.Name);
        Assert.Equal("healthy", peerRecovered?.Name);
        Assert.Equal("restored", recovered!.ReadOutputAs<string>());
        Assert.Equal("restored", peerRecovered!.ReadOutputAs<string>());
        payloadStore.Verify(store => store.DownloadAsync("test-payload", It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.NotSame(initialChannel, refreshedOptions.Channel);
        Assert.Same(refreshedOptions.Channel, factory.Create("deadline").Channel);
        Assert.Equal(1, firstLogger.RecreateCount);
        Assert.Equal(1, peerLogger.RecreateCount);
        Assert.NotEqual(sidecar.StalledConnection, recoveredCall.Connection);
        Assert.Equal(recoveredCall.Connection, peerCall.Connection);
        Assert.All(sidecar.Calls, call =>
        {
            Assert.Equal("test", call.TaskHub);
            Assert.False(string.IsNullOrEmpty(call.Deadline));
        });
        output.WriteLine($"Recreations: first={firstLogger.RecreateCount}, peer={peerLogger.RecreateCount}; "
            + $"connections: stalled={sidecar.StalledConnection}, recovered={recoveredCall.Connection}, peer={peerCall.Connection}");
    }

    async Task AssertDeadlineAsync(DurableTaskClient client, StalledSidecar sidecar)
    {
        using CancellationTokenSource guard = new(Timeout);
        Stopwatch elapsed = Stopwatch.StartNew();
        Task<OrchestrationMetadata?> read = client.GetInstanceAsync("stalled", guard.Token);
        ObservedCall call = await sidecar.NextCallAsync();
        RpcException error = await Assert.ThrowsAsync<RpcException>(() => read.WaitAsync(Timeout));
        Assert.Equal(StatusCode.DeadlineExceeded, error.StatusCode);
        Assert.Equal(sidecar.StalledConnection, call.Connection);
        output.WriteLine($"Stalled GetInstance: {error.StatusCode}, {elapsed.Elapsed.TotalMilliseconds:F0} ms, grpc-timeout={call.Deadline}");
    }

    sealed class RecreationLogger : ILogger
    {
        int recreateCount;

        public int RecreateCount => Volatile.Read(ref this.recreateCount);

        public TaskCompletionSource Recreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id == 80)
            {
                Interlocked.Increment(ref this.recreateCount);
            }
            else if (eventId.Id == 81)
            {
                this.Recreated.TrySetResult();
            }
        }
    }

    sealed record ObservedCall(string Connection, string TaskHub, string Deadline);

    sealed class StalledSidecar : IAsyncDisposable
    {
        readonly Channel<ObservedCall> calls = Channel.CreateUnbounded<ObservedCall>();
        readonly System.Collections.Concurrent.ConcurrentQueue<ObservedCall> observedCalls = new();
        IHost host = null!;
        string? stalledConnection;

        public string Address { get; private set; } = string.Empty;

        public string? StalledConnection => this.stalledConnection;

        public ObservedCall[] Calls => this.observedCalls.ToArray();

        public static async Task<StalledSidecar> StartAsync()
        {
            StalledSidecar sidecar = new();
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

        public Task<ObservedCall> NextCallAsync() => this.calls.Reader.ReadAsync().AsTask().WaitAsync(Timeout);

        public async ValueTask DisposeAsync()
        {
            using IHost host = this.host;
            await host.StopAsync().WaitAsync(Timeout);
        }

        async Task HandleAsync(HttpContext context)
        {
            Assert.Equal("/TaskHubSidecarService/GetInstance", context.Request.Path.Value);
            await context.Request.Body.CopyToAsync(Stream.Null, context.RequestAborted);
            ObservedCall call = new(context.Connection.Id, context.Request.Headers["taskhub"].ToString(),
                context.Request.Headers["grpc-timeout"].ToString());
            this.observedCalls.Enqueue(call);
            Interlocked.CompareExchange(ref this.stalledConnection, call.Connection, null);
            await this.calls.Writer.WriteAsync(call, context.RequestAborted);
            if (call.Connection == this.stalledConnection)
            {
                try
                {
                    await Task.Delay(System.Threading.Timeout.Infinite, context.RequestAborted);
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                {
                    // This connection accepts requests but sends no gRPC response.
                }

                return;
            }

            P.GetInstanceResponse response = new()
            {
                Exists = true,
                OrchestrationState = new()
                {
                    Name = "healthy",
                    InstanceId = "recovered",
                    Output = "test-payload",
                    CreatedTimestamp = Timestamp.FromDateTime(DateTime.UnixEpoch),
                    LastUpdatedTimestamp = Timestamp.FromDateTime(DateTime.UnixEpoch),
                },
            };
            byte[] message = response.ToByteArray();
            byte[] frame = new byte[5 + message.Length];
            BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(1), message.Length);
            message.CopyTo(frame, 5);
            context.Response.ContentType = "application/grpc";
            context.Response.DeclareTrailer("grpc-status");
            await context.Response.Body.WriteAsync(frame, context.RequestAborted);
            context.Response.AppendTrailer("grpc-status", "0");
        }
    }
}