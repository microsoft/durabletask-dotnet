// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using System.Text;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.DurableTask.Tests.Logging;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

/// <summary>
/// Tests verifying that debug-only logging work in <see cref="GrpcDurableTaskWorker"/>'s internal Processor
/// (action-list computation for orchestrator responses and UTF-8 byte counting for activity requests/responses)
/// is skipped when Debug logging is disabled, and produces the expected content when Debug logging is enabled.
/// </summary>
public class GrpcDurableTaskWorkerDebugLoggingTests
{
    const string Category = "Microsoft.DurableTask.Worker.Grpc";
    const int ReceivedActivityRequestEventId = 13;
    const int SendingActivityResponseEventId = 14;
    const int SendingOrchestratorResponseEventId = 11;

    static readonly MethodInfo DispatchWorkItemMethod = typeof(GrpcDurableTaskWorker)
        .GetNestedType("Processor", BindingFlags.NonPublic)!
        .GetMethod("DispatchWorkItem", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Fact]
    public async Task DispatchWorkItem_ActivityRequest_DebugDisabled_DoesNotLogByteCounts()
    {
        // Arrange
        TestLogProvider logProvider = new(new NullOutput());
        DurableTaskWorkerOptions workerOptions = new() { Logging = { UseLegacyCategories = false } };
        GrpcDurableTaskWorker worker = CreateActivityWorker(
            new GrpcDurableTaskWorkerOptions(), workerOptions, new MinLevelLoggerFactory(logProvider, LogLevel.Information));

        P.WorkItem activityWorkItem = CreateActivityWorkItem(input: "42");

        TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<P.TaskHubSidecarService.TaskHubSidecarServiceClient> clientMock = CreateActivityClientMock(completed);
        object processor = CreateProcessor(worker, clientMock.Object);

        // Act
        InvokeDispatchWorkItem(processor, activityWorkItem, CancellationToken.None);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert - the debug-only byte-count logs must not be emitted when Debug logging is disabled.
        logProvider.TryGetLogs(Category, out IReadOnlyCollection<LogEntry>? logs);
        (logs ?? Array.Empty<LogEntry>()).Should().NotContain(
            log => log.EventId.Id == ReceivedActivityRequestEventId || log.EventId.Id == SendingActivityResponseEventId);
    }

    [Fact]
    public async Task DispatchWorkItem_ActivityRequest_DebugEnabled_LogsExactByteCounts()
    {
        // Arrange
        TestLogProvider logProvider = new(new NullOutput());
        DurableTaskWorkerOptions workerOptions = new() { Logging = { UseLegacyCategories = false } };
        GrpcDurableTaskWorker worker = CreateActivityWorker(
            new GrpcDurableTaskWorkerOptions(), workerOptions, new MinLevelLoggerFactory(logProvider, LogLevel.Debug));

        const string input = "42";
        P.WorkItem activityWorkItem = CreateActivityWorkItem(input);

        TaskCompletionSource<P.ActivityResponse> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<P.TaskHubSidecarService.TaskHubSidecarServiceClient> clientMock = CreateActivityClientMock(completed);
        object processor = CreateProcessor(worker, clientMock.Object);

        // Act
        InvokeDispatchWorkItem(processor, activityWorkItem, CancellationToken.None);
        P.ActivityResponse response = await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert - when Debug is enabled, the exact byte counts must still be logged. The expected output size is
        // derived from the actual completed response so the test doesn't depend on data-converter internals.
        int expectedInputSize = Encoding.UTF8.GetByteCount(input);
        int expectedOutputSize = Encoding.UTF8.GetByteCount(response.Result ?? string.Empty);

        logProvider.TryGetLogs(Category, out IReadOnlyCollection<LogEntry>? logs).Should().BeTrue();
        logs!.Should().Contain(log =>
            log.EventId.Id == ReceivedActivityRequestEventId &&
            log.Message.Contains($"with {expectedInputSize} bytes of input data"));
        logs.Should().Contain(log =>
            log.EventId.Id == SendingActivityResponseEventId &&
            log.Message.Contains($"with {expectedOutputSize} bytes of output data"));
    }

    [Fact]
    public async Task DispatchWorkItem_OrchestratorRequest_DebugDisabled_DoesNotLogActionsList()
    {
        // Arrange
        TestLogProvider logProvider = new(new NullOutput());
        DurableTaskWorkerOptions workerOptions = new() { Logging = { UseLegacyCategories = false } };
        GrpcDurableTaskWorker worker = CreateActivityWorker(
            new GrpcDurableTaskWorkerOptions(), workerOptions, new MinLevelLoggerFactory(logProvider, LogLevel.Information));

        P.WorkItem orchestratorWorkItem = CreateOrchestratorNotFoundWorkItem();

        TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<P.TaskHubSidecarService.TaskHubSidecarServiceClient> clientMock = CreateOrchestratorClientMock(completed);
        object processor = CreateProcessor(worker, clientMock.Object);

        // Act
        InvokeDispatchWorkItem(processor, orchestratorWorkItem, CancellationToken.None);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert - the debug-only action-list log must not be emitted when Debug logging is disabled.
        logProvider.TryGetLogs(Category, out IReadOnlyCollection<LogEntry>? logs);
        (logs ?? Array.Empty<LogEntry>()).Should().NotContain(log => log.EventId.Id == SendingOrchestratorResponseEventId);
    }

    [Fact]
    public async Task DispatchWorkItem_OrchestratorRequest_DebugEnabled_LogsActionsList()
    {
        // Arrange
        TestLogProvider logProvider = new(new NullOutput());
        DurableTaskWorkerOptions workerOptions = new() { Logging = { UseLegacyCategories = false } };
        GrpcDurableTaskWorker worker = CreateActivityWorker(
            new GrpcDurableTaskWorkerOptions(), workerOptions, new MinLevelLoggerFactory(logProvider, LogLevel.Debug));

        P.WorkItem orchestratorWorkItem = CreateOrchestratorNotFoundWorkItem();

        TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<P.TaskHubSidecarService.TaskHubSidecarServiceClient> clientMock = CreateOrchestratorClientMock(completed);
        object processor = CreateProcessor(worker, clientMock.Object);

        // Act
        InvokeDispatchWorkItem(processor, orchestratorWorkItem, CancellationToken.None);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert - when Debug is enabled, the single CompleteOrchestration action must still be logged by name.
        logProvider.TryGetLogs(Category, out IReadOnlyCollection<LogEntry>? logs).Should().BeTrue();
        logs!.Should().Contain(log =>
            log.EventId.Id == SendingOrchestratorResponseEventId &&
            log.Message.Contains("Sending 1 action(s) [CompleteOrchestration]"));
    }

    static P.WorkItem CreateActivityWorkItem(string input)
    {
        return new P.WorkItem
        {
            ActivityRequest = new P.ActivityRequest
            {
                Name = "MyActivity",
                TaskId = 42,
                Input = input,
                OrchestrationInstance = new P.OrchestrationInstance
                {
                    InstanceId = "instance1",
                    ExecutionId = "execution1",
                },
            },
            CompletionToken = "completion1",
        };
    }

    static P.WorkItem CreateOrchestratorNotFoundWorkItem()
    {
        P.HistoryEvent executionStarted = new()
        {
            EventId = -1,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ExecutionStarted = new P.ExecutionStartedEvent
            {
                Name = "MissingOrchestrator",
                OrchestrationInstance = new P.OrchestrationInstance
                {
                    InstanceId = "instance1",
                    ExecutionId = "execution1",
                },
            },
        };

        P.OrchestratorRequest orchestratorRequest = new()
        {
            InstanceId = "instance1",
            ExecutionId = "execution1",
            NewEvents = { executionStarted },
            EntityParameters = new P.OrchestratorEntityParameters
            {
                EntityMessageReorderWindow = Duration.FromTimeSpan(TimeSpan.Zero),
            },
        };

        return new P.WorkItem
        {
            OrchestratorRequest = orchestratorRequest,
            CompletionToken = "completion1",
        };
    }

    static GrpcDurableTaskWorker CreateActivityWorker(
        GrpcDurableTaskWorkerOptions grpcOptions,
        DurableTaskWorkerOptions workerOptions,
        ILoggerFactory loggerFactory)
    {
        Mock<IDurableTaskFactory> factoryMock = new(MockBehavior.Strict);
        factoryMock
            .Setup(factory => factory.TryCreateActivity(
                It.Is<TaskName>(name => name.Name == "MyActivity"),
                It.IsAny<IServiceProvider>(),
                out It.Ref<ITaskActivity?>.IsAny))
            .Returns((TaskName name, IServiceProvider serviceProvider, out ITaskActivity? activity) =>
            {
                activity = new TestActivity();
                return true;
            });

        factoryMock
            .Setup(factory => factory.TryCreateOrchestrator(
                It.IsAny<TaskName>(),
                It.IsAny<IServiceProvider>(),
                out It.Ref<ITaskOrchestrator?>.IsAny))
            .Returns(false);

        return new GrpcDurableTaskWorker(
            name: "Test",
            factory: factoryMock.Object,
            grpcOptions: new OptionsMonitorStub<GrpcDurableTaskWorkerOptions>(grpcOptions),
            workerOptions: new OptionsMonitorStub<DurableTaskWorkerOptions>(workerOptions),
            services: new ServiceCollection().BuildServiceProvider(),
            loggerFactory: loggerFactory,
            orchestrationFilter: null,
            exceptionPropertiesProvider: null,
            workItemFiltersMonitor: null);
    }

    static Mock<P.TaskHubSidecarService.TaskHubSidecarServiceClient> CreateActivityClientMock(TaskCompletionSource completed)
    {
        Mock<P.TaskHubSidecarService.TaskHubSidecarServiceClient> clientMock = new(
            MockBehavior.Strict, new object[] { Mock.Of<CallInvoker>() });
        clientMock
            .Setup(client => client.CompleteActivityTaskAsync(
                It.IsAny<P.ActivityResponse>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => completed.TrySetResult())
            .Returns(CreateUnaryCall(Task.FromResult(new P.CompleteTaskResponse())));
        return clientMock;
    }

    static Mock<P.TaskHubSidecarService.TaskHubSidecarServiceClient> CreateActivityClientMock(
        TaskCompletionSource<P.ActivityResponse> completed)
    {
        Mock<P.TaskHubSidecarService.TaskHubSidecarServiceClient> clientMock = new(
            MockBehavior.Strict, new object[] { Mock.Of<CallInvoker>() });
        clientMock
            .Setup(client => client.CompleteActivityTaskAsync(
                It.IsAny<P.ActivityResponse>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Callback<P.ActivityResponse, Metadata, DateTime?, CancellationToken>(
                (response, _, _, _) => completed.TrySetResult(response))
            .Returns(CreateUnaryCall(Task.FromResult(new P.CompleteTaskResponse())));
        return clientMock;
    }

    static Mock<P.TaskHubSidecarService.TaskHubSidecarServiceClient> CreateOrchestratorClientMock(TaskCompletionSource completed)
    {
        Mock<P.TaskHubSidecarService.TaskHubSidecarServiceClient> clientMock = new(
            MockBehavior.Strict, new object[] { Mock.Of<CallInvoker>() });
        clientMock
            .Setup(client => client.CompleteOrchestratorTaskAsync(
                It.IsAny<P.OrchestratorResponse>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => completed.TrySetResult())
            .Returns(CreateUnaryCall(Task.FromResult(new P.CompleteTaskResponse())));
        return clientMock;
    }

    static object CreateProcessor(GrpcDurableTaskWorker worker, P.TaskHubSidecarService.TaskHubSidecarServiceClient client)
    {
        System.Type processorType = typeof(GrpcDurableTaskWorker).GetNestedType("Processor", BindingFlags.NonPublic)!;
        return Activator.CreateInstance(
            processorType,
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            args: new object?[] { worker, client, null, null },
            culture: null)!;
    }

    static void InvokeDispatchWorkItem(object processor, P.WorkItem workItem, CancellationToken cancellationToken)
    {
        DispatchWorkItemMethod.Invoke(processor, new object?[] { workItem, cancellationToken });
    }

    static AsyncUnaryCall<TResponse> CreateUnaryCall<TResponse>(Task<TResponse> responseTask)
    {
        return new AsyncUnaryCall<TResponse>(
            responseTask,
            Task.FromResult(new Metadata()),
            () => new Status(StatusCode.OK, string.Empty),
            () => new Metadata(),
            () => { });
    }

    sealed class TestActivity : ITaskActivity
    {
        public System.Type InputType => typeof(object);

        public System.Type OutputType => typeof(object);

        public Task<object?> RunAsync(TaskActivityContext context, object? input)
        {
            return Task.FromResult<object?>(input);
        }
    }

    /// <summary>
    /// A logger factory that wraps another <see cref="ILoggerFactory"/> and enforces a minimum log level, so tests
    /// can simulate Debug logging being disabled (unlike <see cref="TestLogProvider"/>'s logger, whose
    /// <c>IsEnabled</c> always returns <see langword="true"/>).
    /// </summary>
    sealed class MinLevelLoggerFactory : ILoggerFactory
    {
        readonly ILoggerProvider provider;
        readonly LogLevel minLevel;

        public MinLevelLoggerFactory(ILoggerProvider provider, LogLevel minLevel)
        {
            this.provider = provider;
            this.minLevel = minLevel;
        }

        public void AddProvider(ILoggerProvider loggerProvider)
        {
            // No-op; single provider.
        }

        public ILogger CreateLogger(string categoryName) => new MinLevelLogger(this.provider.CreateLogger(categoryName), this.minLevel);

        public void Dispose()
        {
        }

        sealed class MinLevelLogger : ILogger
        {
            readonly ILogger inner;
            readonly LogLevel minLevel;

            public MinLevelLogger(ILogger inner, LogLevel minLevel)
            {
                this.inner = inner;
                this.minLevel = minLevel;
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => this.inner.BeginScope(state);

            public bool IsEnabled(LogLevel logLevel) => logLevel >= this.minLevel && this.inner.IsEnabled(logLevel);

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (this.IsEnabled(logLevel))
                {
                    this.inner.Log(logLevel, eventId, state, exception, formatter);
                }
            }
        }
    }
}
