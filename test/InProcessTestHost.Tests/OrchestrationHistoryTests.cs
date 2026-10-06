// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.History;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Testing;
using Microsoft.DurableTask.Testing.Sidecar.Grpc;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using P = Microsoft.DurableTask.Protobuf;
using PurgeResult = Microsoft.DurableTask.Client.PurgeResult;

namespace InProcessTestHost.Tests;

/// <summary>
/// Tests history retrieval through the public in-process test host client.
/// </summary>
public class OrchestrationHistoryTests
{
    readonly ITestOutputHelper output;

    public OrchestrationHistoryTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public async Task GetHistoryAsync_CompletedOrchestration_ReturnsOrderedCommittedEvents()
    {
        // Arrange
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(tasks =>
        {
            tasks.AddOrchestratorFunc<string, string>("Greeting", async (context, input) =>
                await context.CallActivityAsync<string>("Greet", input));
            tasks.AddActivityFunc<string, string>("Greet", (context, input) => $"Hello, {input}!");
        }, cancellationToken: timeout.Token);
        string instanceId = await host.Client.ScheduleNewOrchestrationInstanceAsync(
            "Greeting", "Alice", cancellation: timeout.Token);
        OrchestrationMetadata metadata = await host.Client.WaitForInstanceCompletionAsync(
            instanceId, cancellation: timeout.Token);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);

        // Act
        IList<HistoryEvent> history = await host.Client.GetOrchestrationHistoryAsync(instanceId, timeout.Token);

        // Assert
        Assert.Equal(
            new[]
            {
                EventType.OrchestratorStarted, EventType.ExecutionStarted, EventType.TaskScheduled,
                EventType.OrchestratorCompleted, EventType.OrchestratorStarted, EventType.TaskCompleted,
                EventType.ExecutionCompleted, EventType.OrchestratorCompleted,
            },
            history.Select(e => e.EventType));
        ExecutionStartedEvent started = Assert.Single(history.OfType<ExecutionStartedEvent>());
        Assert.Equal(instanceId, started.OrchestrationInstance.InstanceId);
        Assert.Equal("\"Alice\"", started.Input);
        TaskScheduledEvent scheduled = Assert.Single(history.OfType<TaskScheduledEvent>());
        Assert.Equal("Greet", scheduled.Name);
        TaskCompletedEvent activityCompleted = Assert.Single(history.OfType<TaskCompletedEvent>());
        Assert.Equal(scheduled.EventId, activityCompleted.TaskScheduledId);
        ExecutionCompletedEvent completed = Assert.Single(history.OfType<ExecutionCompletedEvent>());
        Assert.Equal(OrchestrationStatus.Completed, completed.OrchestrationStatus);
        Assert.Equal("\"Hello, Alice!\"", completed.Result);
    }

    [Theory]
    [InlineData(32, false)]
    [InlineData(600 * 1024, true)]
    public async Task GetHistoryAsync_RunningAndCompleted_ReturnsFreshSnapshots(int payloadSize, bool streamsHistory)
    {
        // Arrange
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        TaskCompletionSource activityStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseActivity = new(TaskCreationOptions.RunContinuationsAsynchronously);
        HistoryRequestInterceptor interceptor = new();
        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(tasks =>
        {
            tasks.AddOrchestratorFunc<string, int>("PayloadLength", async (context, input) =>
                await context.CallActivityAsync<int>("Length", input));
            tasks.AddActivityFunc<string, int>("Length", async (context, input) =>
            {
                activityStarted.TrySetResult();
                await releaseActivity.Task.WaitAsync(timeout.Token);
                return input.Length;
            });
        }, new DurableTaskTestHostOptions
        {
            ConfigureServices = services => services.Configure<GrpcDurableTaskWorkerOptions>(
                options => options.Interceptors.Add(interceptor)),
        }, timeout.Token);
        string instanceId = await host.Client.ScheduleNewOrchestrationInstanceAsync(
            "PayloadLength", new string('x', payloadSize), cancellation: timeout.Token);
        await activityStarted.Task.WaitAsync(timeout.Token);

        // Act
        IList<HistoryEvent> running;
        try
        {
            running = await host.Client.GetOrchestrationHistoryAsync(instanceId, timeout.Token);
        }
        finally
        {
            releaseActivity.TrySetResult();
        }

        OrchestrationMetadata metadata = await host.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, cancellation: timeout.Token);
        IList<HistoryEvent> completed = await host.Client.GetOrchestrationHistoryAsync(instanceId, timeout.Token);

        // Assert
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal(payloadSize, metadata.ReadOutputAs<int>());
        Assert.Equal(4, running.Count);
        Assert.DoesNotContain(running, e => e is TaskCompletedEvent or ExecutionCompletedEvent);
        int pastEventBytes = running.Sum(e => ProtobufUtils.ToHistoryEventProto(e).CalculateSize());
        this.output.WriteLine($"Committed past-event protobuf size: {pastEventBytes} bytes; worker history requests: {interceptor.HistoryRequestCount}");
        Assert.Equal(streamsHistory, pastEventBytes > 1024 * 1024);
        Assert.Equal(streamsHistory ? 1 : 0, interceptor.HistoryRequestCount);
        Assert.Equal(8, completed.Count);
        Assert.Equal(
            running.Select(ProtobufUtils.ToHistoryEventProto),
            completed.Take(running.Count).Select(ProtobufUtils.ToHistoryEventProto));
        Assert.Single(completed.OfType<TaskCompletedEvent>());
        Assert.Single(completed.OfType<ExecutionCompletedEvent>());
    }

    /// <summary>
    /// Reuses one host without retaining completed episodes' replay snapshots or losing committed history.
    /// </summary>
    [Theory]
    [InlineData(32, false)]
    [InlineData(600 * 1024, false)]
    [InlineData(600 * 1024, true)]
    public async Task GetHistoryAsync_ReusedHost_ReleasesWorkerSnapshots(int payloadSize, bool continueAsNew)
    {
        // Arrange
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        HistoryRequestInterceptor interceptor = new();
        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(tasks =>
        {
            tasks.AddOrchestratorFunc<string, int>("PayloadLength", async (context, input) =>
            {
                int length = await context.CallActivityAsync<int>("Length", input);
                if (continueAsNew && input[0] == 'x')
                {
                    context.ContinueAsNew(new string('y', input.Length));
                }

                return length;
            });
            tasks.AddActivityFunc<string, int>("Length", (context, input) => input.Length);
        }, new DurableTaskTestHostOptions
        {
            ConfigureServices = services => services.Configure<GrpcDurableTaskWorkerOptions>(
                options => options.Interceptors.Add(interceptor)),
        }, timeout.Token);
        string[] instanceIds = new string[2];

        for (int i = 0; i < instanceIds.Length; i++)
        {
            // Act
            string instanceId = await host.Client.ScheduleNewOrchestrationInstanceAsync(
                "PayloadLength", new string('x', payloadSize), cancellation: timeout.Token);
            instanceIds[i] = instanceId;
            OrchestrationMetadata metadata = await host.Client.WaitForInstanceCompletionAsync(
                instanceId, getInputsAndOutputs: true, cancellation: timeout.Token);
            IList<HistoryEvent> history = await host.Client.GetOrchestrationHistoryAsync(instanceId, timeout.Token);

            // Assert
            Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
            Assert.Equal(payloadSize, metadata.ReadOutputAs<int>());
            Assert.Equal(8, history.Count);
            Assert.StartsWith(continueAsNew ? "\"y" : "\"x", Assert.Single(history.OfType<ExecutionStartedEvent>()).Input);
            Assert.Single(history.OfType<TaskCompletedEvent>());
            Assert.Single(history.OfType<ExecutionCompletedEvent>());
            int pastEventBytes = history.Take(4).Sum(e => ProtobufUtils.ToHistoryEventProto(e).CalculateSize());
            bool streamsHistory = payloadSize > 32;
            int streamsPerInstance = streamsHistory ? (continueAsNew ? 2 : 1) : 0;
            this.output.WriteLine(
                $"Instance {i + 1}: past-event protobuf size {pastEventBytes} bytes; worker history requests {interceptor.HistoryRequestCount}");
            Assert.Equal(streamsHistory, pastEventBytes > 1024 * 1024);
            Assert.Equal(streamsPerInstance * (i + 1), interceptor.HistoryRequestCount);
            Assert.Equal(0, WorkerHistorySnapshotTestHelpers.GetSnapshots(host).Count);

            PurgeResult purge = await host.Client.PurgeInstanceAsync(instanceId, cancellation: timeout.Token);
            Assert.Equal(1, purge.PurgedInstanceCount);
            Assert.Null(await host.Client.GetInstanceAsync(instanceId, cancellation: timeout.Token));
            ArgumentException missing = await Assert.ThrowsAsync<ArgumentException>(() =>
                host.Client.GetOrchestrationHistoryAsync(instanceId, timeout.Token));
            Assert.Equal(StatusCode.NotFound, Assert.IsType<RpcException>(missing.InnerException).StatusCode);
            Assert.Equal(0, WorkerHistorySnapshotTestHelpers.GetSnapshots(host).Count);
        }

        Assert.NotEqual(instanceIds[0], instanceIds[1]);
    }

    [Fact]
    public async Task GetHistoryAsync_ContinueAsNew_ReturnsOnlyCurrentGeneration()
    {
        // Arrange
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(tasks =>
        {
            tasks.AddOrchestratorFunc<int, int>("Generations", async (context, input) =>
            {
                int result = await context.CallActivityAsync<int>("Increment", input);
                if (result < 2)
                {
                    context.ContinueAsNew(result);
                }

                return result;
            });
            tasks.AddActivityFunc<int, int>("Increment", (context, input) => input + 1);
        }, cancellationToken: timeout.Token);
        string instanceId = await host.Client.ScheduleNewOrchestrationInstanceAsync(
            "Generations", 0, cancellation: timeout.Token);
        await host.Client.WaitForInstanceCompletionAsync(instanceId, cancellation: timeout.Token);

        // Act
        IList<HistoryEvent> history = await host.Client.GetOrchestrationHistoryAsync(instanceId, timeout.Token);

        // Assert
        Assert.Equal("1", Assert.Single(history.OfType<ExecutionStartedEvent>()).Input);
        Assert.Equal("[1]", Assert.Single(history.OfType<TaskScheduledEvent>()).Input);
        Assert.Equal("2", Assert.Single(history.OfType<TaskCompletedEvent>()).Result);
        Assert.Equal("2", Assert.Single(history.OfType<ExecutionCompletedEvent>()).Result);
    }

    [Fact]
    public async Task GetHistoryAsync_FailedOrchestration_PreservesTerminalStatus()
    {
        // Arrange
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(tasks =>
        {
            tasks.AddOrchestratorFunc<string>("Fail", context =>
                Task.FromException<string>(new InvalidOperationException("Expected test failure")));
        }, cancellationToken: timeout.Token);
        string instanceId = await host.Client.ScheduleNewOrchestrationInstanceAsync(
            "Fail", cancellation: timeout.Token);
        OrchestrationMetadata metadata = await host.Client.WaitForInstanceCompletionAsync(
            instanceId, cancellation: timeout.Token);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);

        // Act
        IList<HistoryEvent> history = await host.Client.GetOrchestrationHistoryAsync(instanceId, timeout.Token);

        // Assert
        Assert.Equal(OrchestrationStatus.Failed,
            Assert.Single(history.OfType<ExecutionCompletedEvent>()).OrchestrationStatus);
    }

    [Fact]
    public async Task GetHistoryAsync_UnknownInstance_ThrowsArgumentException()
    {
        // Arrange
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(
            _ => { }, cancellationToken: timeout.Token);

        // Act
        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            host.Client.GetOrchestrationHistoryAsync("missing", timeout.Token));

        // Assert
        Assert.Equal(StatusCode.NotFound, Assert.IsType<RpcException>(exception.InnerException).StatusCode);
    }

    [Fact]
    public async Task GetHistoryAsync_CanceledRequest_ThrowsOperationCanceledException()
    {
        // Arrange
        using CancellationTokenSource cancellation = new();
        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(_ => { });
        cancellation.Cancel();

        // Act
        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            host.Client.GetOrchestrationHistoryAsync("missing", cancellation.Token));

        // Assert
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    sealed class HistoryRequestInterceptor : Interceptor
    {
        int historyRequestCount;

        public int HistoryRequestCount => Volatile.Read(ref this.historyRequestCount);

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
        {
            if (request is P.StreamInstanceHistoryRequest historyRequest && historyRequest.ForWorkItemProcessing)
            {
                Interlocked.Increment(ref this.historyRequestCount);
            }

            return continuation(request, context);
        }
    }
}
