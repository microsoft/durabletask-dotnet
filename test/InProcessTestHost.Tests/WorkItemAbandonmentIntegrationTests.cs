// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Net;
using DurableTask.Core;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Testing.Sidecar;
using Microsoft.DurableTask.Testing.Sidecar.Grpc;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using P = Microsoft.DurableTask.Protobuf;

namespace InProcessTestHost.Tests;

/// <summary>
/// Tests explicit abandonment and redelivery through localhost gRPC and the in-memory dispatcher.
/// </summary>
public class WorkItemAbandonmentIntegrationTests(ITestOutputHelper output)
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// A rejecting worker releases one delivery, then a compatible SDK worker completes it and unrelated work.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitAbandonment_CompatibleWorkerCompletesRedeliveryAsync(bool activity)
    {
        // Arrange
        InMemoryOrchestrationService service = new();
        WorkItemObserver observer = new();
        using IHost sidecar = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureWebHostDefaults(web => web
                .UseKestrel(options => options.Listen(IPAddress.Loopback, 0, endpoint => endpoint.Protocols = HttpProtocols.Http2))
                .ConfigureServices(services =>
                {
                    services.AddSingleton(observer);
                    services.AddGrpc(options => options.Interceptors.Add<WorkItemObserver>());
                    services.AddSingleton<IOrchestrationService>(service);
                    services.AddSingleton<IOrchestrationServiceClient>(service);
                    services.AddSingleton<TaskHubGrpcServer>();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapGrpcService<TaskHubGrpcServer>());
                }))
            .Build();
        await sidecar.StartAsync().WaitAsync(Timeout);
        string address = Assert.Single(sidecar.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses);
        using GrpcChannel channel = GrpcChannel.ForAddress(address);
        P.TaskHubSidecarService.TaskHubSidecarServiceClient client = new(channel);
        using CancellationTokenSource connectionCancellation = new();
        using AsyncServerStreamingCall<P.WorkItem> rejectingWorker = client.GetWorkItems(
            new(), cancellationToken: connectionCancellation.Token);
        IHost? compatibleWorker = null;
        P.WorkItem? rejected = null;

        try
        {
            await client.StartInstanceAsync(new()
            {
                InstanceId = "rejected",
                Name = "Workflow",
                Version = "2",
                Input = "\"input\"",
            }).ResponseAsync.WaitAsync(Timeout);
            Assert.True(await rejectingWorker.ResponseStream.MoveNext(default).WaitAsync(Timeout));
            rejected = rejectingWorker.ResponseStream.Current;
            if (activity)
            {
                await client.CompleteOrchestratorTaskAsync(new()
                {
                    InstanceId = "rejected",
                    CompletionToken = rejected.CompletionToken,
                    Actions = { new P.OrchestratorAction
                    {
                        Id = 0,
                        ScheduleTask = new() { Name = "Echo", Version = "2", Input = "\"input\"" },
                    } },
                }).ResponseAsync.WaitAsync(Timeout);
                Assert.True(await rejectingWorker.ResponseStream.MoveNext(default).WaitAsync(Timeout));
                rejected = rejectingWorker.ResponseStream.Current;
                Assert.NotNull(rejected.ActivityRequest);
                Assert.Equal("2", rejected.ActivityRequest.Version);
            }
            else
            {
                Assert.Equal("2", Assert.Single(rejected.OrchestratorRequest.NewEvents,
                    historyEvent => historyEvent.ExecutionStarted is not null).ExecutionStarted.Version);
            }

            // Stop fetching before rejecting so the replacement is delivered only to the compatible worker.
            connectionCancellation.Cancel();
            await observer.FirstStreamClosed.Task.WaitAsync(Timeout);

            // Act
            if (activity)
            {
                await client.AbandonTaskActivityWorkItemAsync(
                    new() { CompletionToken = rejected.CompletionToken }).ResponseAsync.WaitAsync(Timeout);
            }
            else
            {
                await client.AbandonTaskOrchestratorWorkItemAsync(
                    new() { CompletionToken = rejected.CompletionToken }).ResponseAsync.WaitAsync(Timeout);
            }

            await client.StartInstanceAsync(new()
            {
                InstanceId = "other",
                Name = "Workflow",
                Version = "2",
                Input = "\"other\"",
            }).ResponseAsync.WaitAsync(Timeout);
            compatibleWorker = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging => logging.ClearProviders())
                .ConfigureServices(services => services.AddDurableTaskWorker(builder =>
                {
                    builder.UseGrpc(channel);
                    builder.UseVersioning(new()
                    {
                        Version = "2",
                        MatchStrategy = DurableTaskWorkerOptions.VersionMatchStrategy.Strict,
                    });
                    builder.AddTasks(tasks =>
                    {
                        tasks.AddOrchestratorFunc<string, string>("Workflow", new TaskVersion("2"),
                            (context, input) => context.CallActivityAsync<string>("Echo", input));
                        tasks.AddActivityFunc<string, string>("Echo", new TaskVersion("2"),
                            (_, input) => Task.FromResult($"completed {input}"));
                    });
                }))
                .Build();
            await compatibleWorker.StartAsync().WaitAsync(Timeout);
            using CancellationTokenSource waitCancellation = new();
            Task<P.GetInstanceResponse> result = client.WaitForInstanceCompletionAsync(
                new() { InstanceId = "rejected", GetInputsAndOutputs = true }, cancellationToken: waitCancellation.Token).ResponseAsync;
            Task<P.GetInstanceResponse> otherResult = client.WaitForInstanceCompletionAsync(
                new() { InstanceId = "other", GetInputsAndOutputs = true }, cancellationToken: waitCancellation.Token).ResponseAsync;
            P.GetInstanceResponse[] completed;
            try
            {
                completed = await Task.WhenAll(result, otherResult).WaitAsync(Timeout);
            }
            finally
            {
                waitCancellation.Cancel();
            }

            // Assert
            Assert.Equal(P.OrchestrationStatus.Completed, completed[0].OrchestrationState.OrchestrationStatus);
            Assert.Equal("\"completed input\"", completed[0].OrchestrationState.Output);
            Assert.Equal(P.OrchestrationStatus.Completed, completed[1].OrchestrationState.OrchestrationStatus);
            Assert.Equal("\"completed other\"", completed[1].OrchestrationState.Output);
            Assert.Equal(1, observer.AbandonmentCalls);
            P.WorkItem[] deliveries = observer.Deliveries.Where(item => activity
                ? item.ActivityRequest?.OrchestrationInstance.InstanceId == "rejected"
                : item.OrchestratorRequest?.InstanceId == "rejected" &&
                    item.OrchestratorRequest.NewEvents.Any(historyEvent => historyEvent.ExecutionStarted is not null)).ToArray();
            output.WriteLine("Delivered work: " + string.Join(", ", observer.Deliveries.Select(item =>
                item.ActivityRequest is { } request
                    ? $"activity:{request.OrchestrationInstance.InstanceId}:{request.TaskId}"
                    : $"orchestrator:{item.OrchestratorRequest.InstanceId}")));
            Assert.Equal(2, deliveries.Length);
            Assert.All(deliveries, delivery => Assert.NotEmpty(delivery.CompletionToken));
            Assert.NotEqual(deliveries[0].CompletionToken, deliveries[1].CompletionToken);
        }
        finally
        {
            connectionCancellation.Cancel();
            if (rejected is not null)
            {
                try
                {
                    if (activity && rejected.ActivityRequest is { } request)
                    {
                        await client.CompleteActivityTaskAsync(new()
                        {
                            InstanceId = request.OrchestrationInstance.InstanceId,
                            TaskId = request.TaskId,
                            CompletionToken = rejected.CompletionToken,
                            Result = "\"cleanup\"",
                        }).ResponseAsync.WaitAsync(Timeout);
                    }
                    else
                    {
                        await client.CompleteOrchestratorTaskAsync(new()
                        {
                            InstanceId = "rejected",
                            CompletionToken = rejected.CompletionToken,
                            Actions = { new P.OrchestratorAction
                            {
                                Id = 0,
                                CompleteOrchestration = new() { OrchestrationStatus = P.OrchestrationStatus.Completed, Result = "\"cleanup\"" },
                            } },
                        }).ResponseAsync.WaitAsync(Timeout);
                    }
                }
                catch (RpcException exception) when (exception.StatusCode == StatusCode.NotFound)
                {
                    // The rejected delivery has already been settled.
                }
            }

            if (compatibleWorker is not null)
            {
                await compatibleWorker.StopAsync().WaitAsync(Timeout);
                compatibleWorker.Dispose();
            }

            await sidecar.StopAsync().WaitAsync(Timeout);
        }
    }

    sealed class WorkItemObserver : Interceptor
    {
        readonly ConcurrentQueue<P.WorkItem> deliveries = new();
        int abandonmentCalls;

        internal TaskCompletionSource FirstStreamClosed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal IEnumerable<P.WorkItem> Deliveries => this.deliveries;

        internal int AbandonmentCalls => Volatile.Read(ref this.abandonmentCalls);

        public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
            TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
        {
            if (request is P.AbandonActivityTaskRequest or P.AbandonOrchestrationTaskRequest)
            {
                Interlocked.Increment(ref this.abandonmentCalls);
            }

            return continuation(request, context);
        }

        public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
            TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context,
            ServerStreamingServerMethod<TRequest, TResponse> continuation)
        {
            try
            {
                await continuation(request, new RecordingWriter<TResponse>(responseStream, item =>
                {
                    if (item is P.WorkItem delivery)
                    {
                        this.deliveries.Enqueue(delivery);
                    }
                }), context);
            }
            finally
            {
                if (request is P.GetWorkItemsRequest)
                {
                    this.FirstStreamClosed.TrySetResult();
                }
            }
        }
    }

    sealed class RecordingWriter<T>(IServerStreamWriter<T> inner, Action<T> record) : IServerStreamWriter<T>
    {
        public WriteOptions? WriteOptions
        {
            get => inner.WriteOptions;
            set => inner.WriteOptions = value;
        }

        public async Task WriteAsync(T message)
        {
            await inner.WriteAsync(message);
            record(message);
        }
    }
}
