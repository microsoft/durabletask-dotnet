// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Shims;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker.Shims;

public class TaskShimLoggingScopeTests
{
    [Fact]
    public async Task TaskActivityShim_RunAsync_UsesInstanceIdScope()
    {
        // Arrange
        string instanceId = Guid.NewGuid().ToString("N");
        IDictionary<string, object?>? scopeState = null;
        Mock<ILogger> loggerMock = new();
        loggerMock.Setup(l => l.BeginScope(It.IsAny<IDictionary<string, object?>>()))
            .Callback((IDictionary<string, object?> state) => scopeState = state)
            .Returns(Mock.Of<IDisposable>());
        loggerMock.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        Mock<ILoggerFactory> loggerFactoryMock = new();
        loggerFactoryMock.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(loggerMock.Object);
        TaskActivityShim shim = new(loggerFactoryMock.Object, JsonDataConverter.Default, new TaskName("TestActivity"), new TestActivity());
        TaskContext coreContext = new(new OrchestrationInstance { InstanceId = instanceId });

        // Act
        await shim.RunAsync(coreContext, "\"input\"");

        // Assert
        scopeState.Should().NotBeNull();
        scopeState!.Should().ContainKey("InstanceId").WhoseValue.Should().Be(instanceId);
    }

    [Fact]
    public async Task TaskOrchestrationShim_Execute_UsesInstanceIdScope()
    {
        // Arrange
        string instanceId = Guid.NewGuid().ToString("N");
        IDictionary<string, object?>? scopeState = null;
        Mock<ILogger> loggerMock = new();
        loggerMock.Setup(l => l.BeginScope(It.IsAny<IDictionary<string, object?>>()))
            .Callback((IDictionary<string, object?> state) => scopeState = state)
            .Returns(Mock.Of<IDisposable>());
        loggerMock.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        Mock<ILoggerFactory> loggerFactoryMock = new();
        loggerFactoryMock.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(loggerMock.Object);
        OrchestrationInvocationContext invocationContext = new(new TaskName("TestOrchestrator"), new DurableTaskWorkerOptions(), loggerFactoryMock.Object);
        TaskOrchestrationShim shim = new(invocationContext, new TestOrchestrator());
        TestOrchestrationContext innerContext = new(instanceId);

        // Act
        await shim.Execute(innerContext, "\"input\"");

        // Assert
        scopeState.Should().NotBeNull();
        scopeState!.Should().ContainKey("InstanceId").WhoseValue.Should().Be(instanceId);
    }

    class TestActivity : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string input)
        {
            return Task.FromResult("ok");
        }
    }

    class TestOrchestrator : TaskOrchestrator<string, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
        {
            return Task.FromResult("ok");
        }
    }

    class TestOrchestrationContext : OrchestrationContext
    {
        public TestOrchestrationContext(string instanceId)
        {
            this.OrchestrationInstance = new OrchestrationInstance
            {
                InstanceId = instanceId,
                ExecutionId = Guid.NewGuid().ToString("N"),
            };
        }

        public override Task<TResult> ScheduleTask<TResult>(string name, string version, params object[] parameters)
        {
            throw new NotImplementedException();
        }

        public override Task<T> CreateTimer<T>(DateTime fireAt, T state)
        {
            throw new NotImplementedException();
        }

        public override Task<T> CreateTimer<T>(DateTime fireAt, T state, CancellationToken cancelToken)
        {
            throw new NotImplementedException();
        }

        public override Task<T> CreateSubOrchestrationInstance<T>(string name, string version, object input)
        {
            throw new NotImplementedException();
        }

        public override Task<T> CreateSubOrchestrationInstance<T>(string name, string version, string instanceId, object input)
        {
            throw new NotImplementedException();
        }

        public override Task<T> CreateSubOrchestrationInstance<T>(string name, string version, string instanceId, object input, IDictionary<string, string> tags)
        {
            throw new NotImplementedException();
        }

        public override void SendEvent(OrchestrationInstance orchestrationInstance, string eventName, object eventData)
        {
            throw new NotImplementedException();
        }

        public override void ContinueAsNew(object input)
        {
            throw new NotImplementedException();
        }

        public override void ContinueAsNew(string newVersion, object input)
        {
            throw new NotImplementedException();
        }
    }
}
