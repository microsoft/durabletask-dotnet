// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Grpc.Core;
using Microsoft.DurableTask.Tests.Logging;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

public class ExecuteWithRetryTests
{
    const string Category = "Microsoft.DurableTask.Worker.Grpc";

    static readonly MethodInfo ExecuteWithRetryAsyncMethod = FindExecuteWithRetryAsyncMethod();

    static Type FindProcessorType()
    {
        return typeof(GrpcDurableTaskWorker)
            .GetNestedTypes(BindingFlags.NonPublic)
            .Single(type => type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Any(method =>
                    method.ReturnType == typeof(Task) &&
                    method.GetParameters() is var parameters &&
                    parameters.Length == 3 &&
                    parameters[0].ParameterType == typeof(Func<Task>) &&
                    parameters[1].ParameterType == typeof(string) &&
                    parameters[2].ParameterType == typeof(CancellationToken)));
    }

    static MethodInfo FindExecuteWithRetryAsyncMethod()
    {
        return FindProcessorType()
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(method =>
                method.ReturnType == typeof(Task) &&
                method.GetParameters() is var parameters &&
                parameters.Length == 3 &&
                parameters[0].ParameterType == typeof(Func<Task>) &&
                parameters[1].ParameterType == typeof(string) &&
                parameters[2].ParameterType == typeof(CancellationToken));
    }
    [Fact]
    public async Task ExecuteWithRetryAsync_SucceedsOnFirstAttempt_DoesNotRetry()
    {
        // Arrange
        object processor = CreateProcessor();
        int callCount = 0;

        // Act
        await InvokeExecuteWithRetryAsync(
            processor,
            () => { callCount++; return Task.CompletedTask; },
            "TestOperation",
            CancellationToken.None);

        // Assert
        callCount.Should().Be(1);
    }

    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.Unknown)]
    [InlineData(StatusCode.DeadlineExceeded)]
    [InlineData(StatusCode.Internal)]
    public async Task ExecuteWithRetryAsync_TransientError_RetriesAndEventuallySucceeds(StatusCode statusCode)
    {
        // Arrange
        object processor = CreateProcessor();
        int callCount = 0;

        // Act - fail once then succeed
        await InvokeExecuteWithRetryAsync(
            processor,
            () =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new RpcException(new Status(statusCode, "transient error"));
                }

                return Task.CompletedTask;
            },
            "TestOperation",
            CancellationToken.None);

        // Assert
        callCount.Should().Be(2);
    }

    [Theory]
    [InlineData(StatusCode.InvalidArgument)]
    [InlineData(StatusCode.AlreadyExists)]
    [InlineData(StatusCode.PermissionDenied)]
    public async Task ExecuteWithRetryAsync_NonTransientError_ThrowsWithoutRetrying(StatusCode statusCode)
    {
        // Arrange
        object processor = CreateProcessor();
        int callCount = 0;

        // Act
        Func<Task> act = () => InvokeExecuteWithRetryAsync(
            processor,
            () =>
            {
                callCount++;
                throw new RpcException(new Status(statusCode, "non-transient error"));
            },
            "TestOperation",
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<RpcException>().Where(e => e.StatusCode == statusCode);
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_CancellationRequestedDuringRetryDelay_ThrowsOperationCanceledException()
    {
        // Arrange
        using CancellationTokenSource cts = new();
        object processor = CreateProcessor();

        // Act - cancel immediately after first failure so the retry delay is cancelled
        Func<Task> act = () => InvokeExecuteWithRetryAsync(
            processor,
            () =>
            {
                cts.Cancel();
                throw new RpcException(new Status(StatusCode.Unavailable, "transient error"));
            },
            "TestOperation",
            cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_TransientError_LogsRetryAttempt()
    {
        // Arrange
        TestLogProvider logProvider = new(new NullOutput());
        object processor = CreateProcessor(logProvider);
        int callCount = 0;
        const string operationName = "CompleteOrchestratorTaskAsync";

        // Act - fail once then succeed
        await InvokeExecuteWithRetryAsync(
            processor,
            () =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new RpcException(new Status(StatusCode.Unavailable, "transient error"));
                }

                return Task.CompletedTask;
            },
            operationName,
            CancellationToken.None);

        // Assert
        logProvider.TryGetLogs(Category, out IReadOnlyCollection<LogEntry>? logs).Should().BeTrue();
        logs!.Should().Contain(log =>
            log.Message.Contains($"Transient gRPC error for '{operationName}'") &&
            log.Message.Contains("Attempt 1 of 10"));
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_MultipleTransientErrors_LogsEachRetryAttempt()
    {
        // Arrange
        TestLogProvider logProvider = new(new NullOutput());
        object processor = CreateProcessor(logProvider);
        int callCount = 0;
        const string operationName = "CompleteActivityTaskAsync";

        // Act - fail twice then succeed
        await InvokeExecuteWithRetryAsync(
            processor,
            () =>
            {
                callCount++;
                if (callCount < 3)
                {
                    throw new RpcException(new Status(StatusCode.Unavailable, "transient error"));
                }

                return Task.CompletedTask;
            },
            operationName,
            CancellationToken.None);

        // Assert
        logProvider.TryGetLogs(Category, out IReadOnlyCollection<LogEntry>? logs).Should().BeTrue();
        logs!.Should().Contain(log =>
            log.Message.Contains($"Transient gRPC error for '{operationName}'") &&
            log.Message.Contains("Attempt 1 of 10"));
        logs.Should().Contain(log =>
            log.Message.Contains($"Transient gRPC error for '{operationName}'") &&
            log.Message.Contains("Attempt 2 of 10"));
        callCount.Should().Be(3);
    }

    static object CreateProcessor(TestLogProvider? logProvider = null)
    {
        ILoggerFactory loggerFactory = logProvider is null
            ? NullLoggerFactory.Instance
            : new SimpleLoggerFactory(logProvider);

        Mock<IDurableTaskFactory> factoryMock = new(MockBehavior.Strict);
        GrpcDurableTaskWorkerOptions grpcOptions = new();
        DurableTaskWorkerOptions workerOptions = new()
        {
            Logging = { UseLegacyCategories = false },
        };

        GrpcDurableTaskWorker worker = new(
            name: "Test",
            factory: factoryMock.Object,
            grpcOptions: new OptionsMonitorStub<GrpcDurableTaskWorkerOptions>(grpcOptions),
            workerOptions: new OptionsMonitorStub<DurableTaskWorkerOptions>(workerOptions),
            services: Mock.Of<IServiceProvider>(),
            loggerFactory: loggerFactory,
            orchestrationFilter: null,
            exceptionPropertiesProvider: null);

        CallInvoker callInvoker = Mock.Of<CallInvoker>();
        P.TaskHubSidecarService.TaskHubSidecarServiceClient client = new(callInvoker);

        Type processorType = typeof(GrpcDurableTaskWorker).GetNestedType("Processor", BindingFlags.NonPublic)!;
        return Activator.CreateInstance(
            processorType,
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            args: new object?[] { worker, client, null, null },
            culture: null)!;
    }

    static Task InvokeExecuteWithRetryAsync(
        object processor,
        Func<Task> action,
        string operationName,
        CancellationToken cancellationToken)
    {
        return (Task)ExecuteWithRetryAsyncMethod.Invoke(
            processor,
            new object?[] { action, operationName, cancellationToken })!;
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

        public SimpleLoggerFactory(ILoggerProvider provider) => this.provider = provider;

        public void AddProvider(ILoggerProvider provider) { }

        public ILogger CreateLogger(string categoryName) => this.provider.CreateLogger(categoryName);

        public void Dispose() { }
    }

    sealed class NullOutput : ITestOutputHelper
    {
        public void WriteLine(string message) { }
        public void WriteLine(string format, params object[] args) { }
    }
}
