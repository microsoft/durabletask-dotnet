// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using FluentAssertions;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.DurableTask.Tests.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using P = Microsoft.DurableTask.Protobuf;
using Xunit;
using Grpc.Core;
using Xunit.Abstractions;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

public class RunBackgroundTaskLoggingTests
{
    const string Category = "Microsoft.DurableTask";

    [Fact]
    public async Task Logs_Abandoning_And_Abandoned_For_Orchestrator()
    {
        await using var fixture = await TestFixture.CreateAsync();

        string instanceId = Guid.NewGuid().ToString("N");
        string completionToken = Guid.NewGuid().ToString("N");

        var tcs = new TaskCompletionSource<bool>();
        fixture.ClientMock
            .Setup(c => c.AbandonTaskOrchestratorWorkItemAsync(
                It.IsAny<P.AbandonOrchestrationTaskRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns((P.AbandonOrchestrationTaskRequest r, Metadata h, DateTime? d, CancellationToken ct) =>
                CompletedAsyncUnaryCall(new P.AbandonOrchestrationTaskResponse(), () => tcs.TrySetResult(true)));

        P.WorkItem workItem = new()
        {
            OrchestratorRequest = new P.OrchestratorRequest { InstanceId = instanceId },
            CompletionToken = completionToken,
        };

        fixture.InvokeRunBackgroundTask(workItem, () => Task.FromException(new Exception("boom")));

        await WaitAsync(tcs.Task);

        await AssertEventually(() => fixture.GetLogs().Any(l => l.Message.Contains("Abandoning orchestrator work item") && l.Message.Contains(instanceId)));
        await AssertEventually(() => fixture.GetLogs().Any(l => l.Message.Contains("Abandoned orchestrator work item") && l.Message.Contains(instanceId)));
    }

    [Fact]
    public async Task Logs_Abandoning_And_NoAbandoned_When_Orchestrator_Abandon_Fails()
    {
        await using var fixture = await TestFixture.CreateAsync();

        string instanceId = Guid.NewGuid().ToString("N");
        string completionToken = Guid.NewGuid().ToString("N");

        fixture.ClientMock
            .Setup(c => c.AbandonTaskOrchestratorWorkItemAsync(
                It.IsAny<P.AbandonOrchestrationTaskRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => FaultedAsyncUnaryCall<P.AbandonOrchestrationTaskResponse>(new Exception("abandon failure")));

        P.WorkItem workItem = new()
        {
            OrchestratorRequest = new P.OrchestratorRequest { InstanceId = instanceId },
            CompletionToken = completionToken,
        };

        fixture.InvokeRunBackgroundTask(workItem, () => Task.FromException(new Exception("boom")));

        // Allow background task to execute
        await Task.Delay(200);

        await AssertEventually(() => fixture.GetLogs().Any(l => l.Message.Contains("Abandoning orchestrator work item") && l.Message.Contains(instanceId)));
        Assert.DoesNotContain(fixture.GetLogs(), l => l.Message.Contains("Abandoned orchestrator work item") && l.Message.Contains(instanceId));
        await AssertEventually(() => fixture.GetLogs().Any(l => l.Message.Contains("Unexpected error") && l.Message.Contains(instanceId)));
    }

    [Fact]
    public async Task Logs_Abandoning_And_Abandoned_For_Activity()
    {
        await using var fixture = await TestFixture.CreateAsync();

        string instanceId = Guid.NewGuid().ToString("N");
        string completionToken = Guid.NewGuid().ToString("N");

        var tcs = new TaskCompletionSource<bool>();
        fixture.ClientMock
            .Setup(c => c.AbandonTaskActivityWorkItemAsync(
                It.IsAny<P.AbandonActivityTaskRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns((P.AbandonActivityTaskRequest r, Metadata h, DateTime? d, CancellationToken ct) =>
                CompletedAsyncUnaryCall(new P.AbandonActivityTaskResponse(), () => tcs.TrySetResult(true)));

        P.WorkItem workItem = new()
        {
            ActivityRequest = new P.ActivityRequest
            {
                Name = "MyActivity",
                TaskId = 42,
                OrchestrationInstance = new P.OrchestrationInstance { InstanceId = instanceId },
            },
            CompletionToken = completionToken,
        };

        fixture.InvokeRunBackgroundTask(workItem, () => Task.FromException(new Exception("boom")));

        await WaitAsync(tcs.Task);

        await AssertEventually(() => fixture.GetLogs().Any(l => l.Message.Contains("Abandoning activity work item") && l.Message.Contains(instanceId)));
        await AssertEventually(() => fixture.GetLogs().Any(l => l.Message.Contains("Abandoned activity work item") && l.Message.Contains(instanceId)));
    }

    [Fact]
    public async Task Logs_Abandoning_And_NoAbandoned_When_Activity_Abandon_Fails()
    {
        await using var fixture = await TestFixture.CreateAsync();

        string instanceId = Guid.NewGuid().ToString("N");
        string completionToken = Guid.NewGuid().ToString("N");

        fixture.ClientMock
            .Setup(c => c.AbandonTaskActivityWorkItemAsync(
                It.IsAny<P.AbandonActivityTaskRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => FaultedAsyncUnaryCall<P.AbandonActivityTaskResponse>(new Exception("abandon failure")));

        P.WorkItem workItem = new()
        {
            ActivityRequest = new P.ActivityRequest
            {
                Name = "MyActivity",
                TaskId = 42,
                OrchestrationInstance = new P.OrchestrationInstance { InstanceId = instanceId },
            },
            CompletionToken = completionToken,
        };

        fixture.InvokeRunBackgroundTask(workItem, () => Task.FromException(new Exception("boom")));

        await Task.Delay(200);

        await AssertEventually(() => fixture.GetLogs().Any(l => l.Message.Contains("Abandoning activity work item") && l.Message.Contains(instanceId)));
        Assert.DoesNotContain(fixture.GetLogs(), l => l.Message.Contains("Abandoned activity work item") && l.Message.Contains(instanceId));
        await AssertEventually(() => fixture.GetLogs().Any(l => l.Message.Contains("Unexpected error") && l.Message.Contains(instanceId)));
    }

    [Fact]
    public async Task Logs_Abandoning_And_Abandoned_For_Entity_V1()
    {
        await using var fixture = await TestFixture.CreateAsync();

        string instanceId = "entity@key";
        string completionToken = Guid.NewGuid().ToString("N");

        var tcs = new TaskCompletionSource<bool>();
        fixture.ClientMock
            .Setup(c => c.AbandonTaskEntityWorkItemAsync(
                It.IsAny<P.AbandonEntityTaskRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns((P.AbandonEntityTaskRequest r, Metadata h, DateTime? d, CancellationToken ct) =>
                CompletedAsyncUnaryCall(new P.AbandonEntityTaskResponse(), () => tcs.TrySetResult(true)));

        P.WorkItem workItem = new()
        {
            EntityRequest = new P.EntityBatchRequest { InstanceId = instanceId },
            CompletionToken = completionToken,
        };

        fixture.InvokeRunBackgroundTask(workItem, () => Task.FromException(new Exception("boom")));

        await WaitAsync(tcs.Task);

        await AssertEventually(() => fixture.GetLogs().Any(l => l.Message.Contains("Abandoning entity work item") && l.Message.Contains(instanceId)));
        await AssertEventually(() => fixture.GetLogs().Any(l => l.Message.Contains("Abandoned entity work item") && l.Message.Contains(instanceId)));
    }

    [Fact]
    public async Task Logs_Abandoning_And_NoAbandoned_When_EntityV1_Abandon_Fails()
    {
        await using var fixture = await TestFixture.CreateAsync();

        string instanceId = "entity@key";
        string completionToken = Guid.NewGuid().ToString("N");

        fixture.ClientMock
            .Setup(c => c.AbandonTaskEntityWorkItemAsync(
                It.IsAny<P.AbandonEntityTaskRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => FaultedAsyncUnaryCall<P.AbandonEntityTaskResponse>(new Exception("abandon failure")));

        P.WorkItem workItem = new()
        {
            EntityRequest = new P.EntityBatchRequest { InstanceId = instanceId },
            CompletionToken = completionToken,
        };

        fixture.InvokeRunBackgroundTask(workItem, () => Task.FromException(new Exception("boom")));

        await Task.Delay(200);

        await AssertEventually(() => fixture.GetLogs().Any(l => l.Message.Contains("Abandoning entity work item") && l.Message.Contains(instanceId)));
        Assert.DoesNotContain(fixture.GetLogs(), l => l.Message.Contains("Abandoned entity work item") && l.Message.Contains(instanceId));
        await AssertEventually(() => fixture.GetLogs().Any(l => l.Message.Contains("Unexpected error") && l.Message.Contains(instanceId)));
    }

    [Fact]
    public async Task Logs_Abandoning_And_Abandoned_For_Entity_V2()
    {
        await using var fixture = await TestFixture.CreateAsync();

        string instanceId = "entity2@key";
        string completionToken = Guid.NewGuid().ToString("N");

        var tcs = new TaskCompletionSource<bool>();
        fixture.ClientMock
            .Setup(c => c.AbandonTaskEntityWorkItemAsync(
                It.IsAny<P.AbandonEntityTaskRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns((P.AbandonEntityTaskRequest r, Metadata h, DateTime? d, CancellationToken ct) =>
                CompletedAsyncUnaryCall(new P.AbandonEntityTaskResponse(), () => tcs.TrySetResult(true)));

        P.WorkItem workItem = new()
        {
            EntityRequestV2 = new P.EntityRequest { InstanceId = instanceId },
            CompletionToken = completionToken,
        };

        fixture.InvokeRunBackgroundTask(workItem, () => Task.FromException(new Exception("boom")));

        await WaitAsync(tcs.Task);

        await AssertEventually(() => fixture.GetLogs().Any(l => l.Message.Contains("Abandoning entity work item") && l.Message.Contains(instanceId)));
        await AssertEventually(() => fixture.GetLogs().Any(l => l.Message.Contains("Abandoned entity work item") && l.Message.Contains(instanceId)));
    }

    [Fact]
    public async Task Logs_Abandoning_And_NoAbandoned_When_EntityV2_Abandon_Fails()
    {
        await using var fixture = await TestFixture.CreateAsync();

        string instanceId = "entity2@key";
        string completionToken = Guid.NewGuid().ToString("N");

        fixture.ClientMock
            .Setup(c => c.AbandonTaskEntityWorkItemAsync(
                It.IsAny<P.AbandonEntityTaskRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => FaultedAsyncUnaryCall<P.AbandonEntityTaskResponse>(new Exception("abandon failure")));

        P.WorkItem workItem = new()
        {
            EntityRequestV2 = new P.EntityRequest { InstanceId = instanceId },
            CompletionToken = completionToken,
        };

        fixture.InvokeRunBackgroundTask(workItem, () => Task.FromException(new Exception("boom")));

        await Task.Delay(200);

        await AssertEventually(() => fixture.GetLogs().Any(l => l.Message.Contains("Abandoning entity work item") && l.Message.Contains(instanceId)));
        Assert.DoesNotContain(fixture.GetLogs(), l => l.Message.Contains("Abandoned entity work item") && l.Message.Contains(instanceId));
        await AssertEventually(() => fixture.GetLogs().Any(l => l.Message.Contains("Unexpected error") && l.Message.Contains(instanceId)));
    }

    [Fact]
    public async Task Forwards_CancellationToken_To_Abandon_Orchestrator()
    {
        await using var fixture = await TestFixture.CreateAsync();

        string instanceId = Guid.NewGuid().ToString("N");
        string completionToken = Guid.NewGuid().ToString("N");

        var cts = new CancellationTokenSource();
        var observed = new TaskCompletionSource<bool>();

        fixture.ClientMock
            .Setup(c => c.AbandonTaskOrchestratorWorkItemAsync(
                It.IsAny<P.AbandonOrchestrationTaskRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns((P.AbandonOrchestrationTaskRequest r, Metadata h, DateTime? d, CancellationToken ct) =>
            {
                if (ct == cts.Token)
                {
                    observed.TrySetResult(true);
                }
                return CompletedAsyncUnaryCall(new P.AbandonOrchestrationTaskResponse());
            });

        P.WorkItem workItem = new()
        {
            OrchestratorRequest = new P.OrchestratorRequest { InstanceId = instanceId },
            CompletionToken = completionToken,
        };

        fixture.InvokeRunBackgroundTask(workItem, () => Task.FromException(new Exception("boom")), cts.Token);

        await WaitAsync(observed.Task);
    }

    static async Task WaitAsync(Task task)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task completed = await Task.WhenAny(task, Task.Delay(Timeout.InfiniteTimeSpan, cts.Token));
        if (completed != task)
        {
            throw new TimeoutException("Timed out waiting for abandon call");
        }
    }

    static async Task AssertEventually(Func<bool> condition, int timeoutMs = 2000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
            {
                return;
            }
            await Task.Delay(50);
        }
        Assert.True(false, "Condition not met within timeout");
    }

    sealed class TestFixture : IAsyncDisposable
    {
        readonly ServiceProvider services;
        readonly TestLogProvider logProvider;

        public Mock<P.TaskHubSidecarService.TaskHubSidecarServiceClient> ClientMock { get; }
        public GrpcDurableTaskWorker Worker { get; }
        object ProcessorInstance { get; }
        MethodInfo RunBackgroundTaskMethod { get; }

        TestFixture(ServiceProvider services, TestLogProvider logProvider, Mock<P.TaskHubSidecarService.TaskHubSidecarServiceClient> clientMock, GrpcDurableTaskWorker worker, object processorInstance, MethodInfo runBackgroundTaskMethod)
        {
            this.services = services;
            this.logProvider = logProvider;
            this.ClientMock = clientMock;
            this.Worker = worker;
            this.ProcessorInstance = processorInstance;
            this.RunBackgroundTaskMethod = runBackgroundTaskMethod;
        }

        public static async Task<TestFixture> CreateAsync()
        {
            // Logging
            var logProvider = new TestLogProvider(new NullOutput());
            // DI
            var services = new ServiceCollection().BuildServiceProvider();
            var loggerFactory = new SimpleLoggerFactory(logProvider);

            // Options
            var grpcOptions = new OptionsMonitorStub<GrpcDurableTaskWorkerOptions>(new GrpcDurableTaskWorkerOptions());
            var workerOptions = new OptionsMonitorStub<DurableTaskWorkerOptions>(new DurableTaskWorkerOptions());

            // Factory (not used in these tests)
            var factoryMock = new Mock<IDurableTaskFactory>(MockBehavior.Strict);

            // Worker
            var worker = new GrpcDurableTaskWorker(
                name: "Test",
                factory: factoryMock.Object,
                grpcOptions: grpcOptions,
                workerOptions: workerOptions,
                services: services,
                loggerFactory: loggerFactory);

            // Client mock
            var callInvoker = Mock.Of<CallInvoker>();
            var clientMock = new Mock<P.TaskHubSidecarService.TaskHubSidecarServiceClient>(MockBehavior.Strict, new object[] { callInvoker });

            // Build Processor via reflection
            Type processorType = typeof(GrpcDurableTaskWorker).GetNestedType("Processor", BindingFlags.NonPublic)!;
            object processorInstance = Activator.CreateInstance(
                processorType,
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                args: new object?[] { worker, clientMock.Object, null },
                culture: null)!;

            MethodInfo runBackgroundTask = processorType.GetMethod("RunBackgroundTask", BindingFlags.Instance | BindingFlags.NonPublic)!;

            return new TestFixture((ServiceProvider)services, logProvider, clientMock, worker, processorInstance, runBackgroundTask);
        }

        public void InvokeRunBackgroundTask(P.WorkItem workItem, Func<Task> handler, CancellationToken cancellationToken = default)
        {
            this.RunBackgroundTaskMethod.Invoke(this.ProcessorInstance, new object?[] { workItem, handler, cancellationToken });
        }

        public IReadOnlyCollection<LogEntry> GetLogs()
        {
            this.logProvider.TryGetLogs(Category, out var logs);
            return logs ?? Array.Empty<LogEntry>();
        }

        public ValueTask DisposeAsync()
        {
            (this.services as IDisposable)?.Dispose();
            return default;
        }
    }

    static AsyncUnaryCall<T> CompletedAsyncUnaryCall<T>(T response, Action? onInvoke = null)
    {
        var respTask = Task.Run(() => { onInvoke?.Invoke(); return response; });
        return new AsyncUnaryCall<T>(
            respTask,
            Task.FromResult(new Metadata()),
            () => new Status(StatusCode.OK, string.Empty),
            () => new Metadata(),
            () => { });
    }

    static AsyncUnaryCall<T> FaultedAsyncUnaryCall<T>(Exception ex)
    {
        var respTask = Task.FromException<T>(ex);
        return new AsyncUnaryCall<T>(
            respTask,
            Task.FromResult(new Metadata()),
            () => new Status(StatusCode.Unknown, ex.Message),
            () => new Metadata(),
            () => { });
    }

    sealed class NullOutput : ITestOutputHelper
    {
        public void WriteLine(string message) { }
        public void WriteLine(string format, params object[] args) { }
    }

    sealed class OptionsMonitorStub<T> : IOptionsMonitor<T> where T : class, new()
    {
        readonly T value;

        public OptionsMonitorStub(T value) => this.value = value;

        public T CurrentValue => this.value;

        public T Get(string? name) => this.value;

        public IDisposable OnChange(Action<T, string?> listener) => NullDisposable.Instance;

        sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }

    sealed class SimpleLoggerFactory : ILoggerFactory
    {
        readonly ILoggerProvider provider;

        public SimpleLoggerFactory(ILoggerProvider provider)
        {
            this.provider = provider;
        }

        public void AddProvider(ILoggerProvider provider)
        {
            // No-op; single provider
        }

        public ILogger CreateLogger(string categoryName) => this.provider.CreateLogger(categoryName);

        public void Dispose() { }
    }
}


