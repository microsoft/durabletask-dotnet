// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.DurableTask.Worker.Shims;

public class TaskOrchestrationContextWrapperTests
{
    [Fact]
    public void Ctor_NullParent_Populates()
    {
        TestOrchestrationContext innerContext = new();
        OrchestrationInvocationContext invocationContext = new("Test", new(), NullLoggerFactory.Instance, null);
        string input = "test-input";

        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, input);

        VerifyWrapper(wrapper, innerContext, invocationContext, input);
    }

    [Fact]
    public void Ctor_NonNullParent_Populates()
    {
        TestOrchestrationContext innerContext = new();
        ParentOrchestrationInstance parent = new("Parent", Guid.NewGuid().ToString());
        OrchestrationInvocationContext invocationContext = new("Test", new(), NullLoggerFactory.Instance, parent);
        string input = "test-input";

        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, input);

        VerifyWrapper(wrapper, innerContext, invocationContext, input);
    }

    static void VerifyWrapper<T>(
        TaskOrchestrationContextWrapper wrapper,
        OrchestrationContext innerContext,
        OrchestrationInvocationContext invocationContext,
        T input)
    {
        wrapper.Name.Should().Be(invocationContext.Name);
        wrapper.InstanceId.Should().Be(innerContext.OrchestrationInstance.InstanceId);
        wrapper.Parent.Should().Be(invocationContext.Parent);
        wrapper.IsReplaying.Should().Be(false);
        wrapper.GetInput<T>().Should().Be(input);
    }

    [Fact]
    public void ContinueAsNew_WithoutVersion_CallsInnerContextWithoutVersion()
    {
        // Arrange
        TrackingOrchestrationContext innerContext = new();
        OrchestrationInvocationContext invocationContext = new("Test", new(), NullLoggerFactory.Instance, null);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        // Act
        wrapper.ContinueAsNew("new-input", preserveUnprocessedEvents: false);

        // Assert
        innerContext.LastContinueAsNewInput.Should().Be("new-input");
        innerContext.LastContinueAsNewVersion.Should().BeNull();
    }

    [Fact]
    public void ContinueAsNew_WithVersion_CallsInnerContextWithVersion()
    {
        // Arrange
        TrackingOrchestrationContext innerContext = new();
        OrchestrationInvocationContext invocationContext = new("Test", new(), NullLoggerFactory.Instance, null);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        // Act
        wrapper.ContinueAsNew(new ContinueAsNewOptions
        {
            NewVersion = "v2",
            NewInput = "new-input",
            PreserveUnprocessedEvents = false,
        });

        // Assert
        innerContext.LastContinueAsNewInput.Should().Be("new-input");
        innerContext.LastContinueAsNewVersion.Should().Be("v2");
    }

    [Fact]
    public void ContinueAsNew_WithOptionsNoVersion_CallsInnerContextWithoutVersion()
    {
        // Arrange
        TrackingOrchestrationContext innerContext = new();
        OrchestrationInvocationContext invocationContext = new("Test", new(), NullLoggerFactory.Instance, null);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        // Act
        wrapper.ContinueAsNew(new ContinueAsNewOptions
        {
            NewInput = "new-input",
            PreserveUnprocessedEvents = false,
        });

        // Assert
        innerContext.LastContinueAsNewInput.Should().Be("new-input");
        innerContext.LastContinueAsNewVersion.Should().BeNull();
    }

    [Fact]
    public async Task CallActivityAsync_ActivityOptionsVersionOverridesInheritedOrchestrationVersion()
    {
        // Arrange
        TrackingOrchestrationContext innerContext = new("v2");
        OrchestrationInvocationContext invocationContext = new("Test", new(), NullLoggerFactory.Instance, null);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        // Act
        await wrapper.CallActivityAsync<string>(
            "TestActivity",
            123,
            new ActivityOptions
            {
                Version = "v1",
            });

        // Assert
        innerContext.LastScheduledTaskName.Should().Be("TestActivity");
        innerContext.LastScheduledTaskVersion.Should().Be("v1");
        innerContext.LastScheduledTaskInput.Should().Be(123);
    }

    [Fact]
    public async Task CallActivityAsync_ActivityOptionsVersionOverridesInheritedOrchestrationVersion_WithRetryPolicy()
    {
        // Arrange
        TrackingOrchestrationContext innerContext = new("v2");
        OrchestrationInvocationContext invocationContext = new("Test", new(), NullLoggerFactory.Instance, null);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        // Act
        await wrapper.CallActivityAsync<string>(
            "TestActivity",
            123,
            new ActivityOptions(new RetryPolicy(1, TimeSpan.FromSeconds(1)))
            {
                Version = "v1",
            });

        // Assert
        innerContext.LastScheduledTaskName.Should().Be("TestActivity");
        innerContext.LastScheduledTaskVersion.Should().Be("v1");
        innerContext.LastScheduledTaskInput.Should().Be(123);
    }

    [Fact]
    public async Task CallActivityAsync_PlainTaskOptionsUsesInheritedOrchestrationVersion()
    {
        // Arrange
        TrackingOrchestrationContext innerContext = new("v2");
        OrchestrationInvocationContext invocationContext = new("Test", new(), NullLoggerFactory.Instance, null);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        // Act
        await wrapper.CallActivityAsync<string>("TestActivity", 123, new TaskOptions());

        // Assert
        innerContext.LastScheduledTaskName.Should().Be("TestActivity");
        innerContext.LastScheduledTaskVersion.Should().Be("v2");
        innerContext.LastScheduledTaskInput.Should().Be(123);
    }

    [Fact]
    public async Task CallActivityAsync_NullOptionsUsesInheritedOrchestrationVersion()
    {
        // Arrange
        TrackingOrchestrationContext innerContext = new("v2");
        OrchestrationInvocationContext invocationContext = new("Test", new(), NullLoggerFactory.Instance, null);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        // Act
        await wrapper.CallActivityAsync<string>("TestActivity", 123);

        // Assert
        innerContext.LastScheduledTaskName.Should().Be("TestActivity");
        innerContext.LastScheduledTaskVersion.Should().Be("v2");
        innerContext.LastScheduledTaskInput.Should().Be(123);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(true, "   ")]
    public async Task CallActivityAsync_MissingOrEmptyActivityVersionUsesInheritedOrchestrationVersion(
        bool setVersion,
        string? explicitVersion)
    {
        // Arrange
        TrackingOrchestrationContext innerContext = new("v2");
        OrchestrationInvocationContext invocationContext = new("Test", new(), NullLoggerFactory.Instance, null);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");
        ActivityOptions options = new();

        if (setVersion)
        {
            options = options with
            {
                Version = explicitVersion is null ? default(TaskVersion?) : new TaskVersion(explicitVersion),
            };
        }

        // Act
        await wrapper.CallActivityAsync<string>("TestActivity", 123, options);

        // Assert
        innerContext.LastScheduledTaskName.Should().Be("TestActivity");
        innerContext.LastScheduledTaskVersion.Should().Be("v2");
        innerContext.LastScheduledTaskInput.Should().Be(123);
    }

    sealed class TrackingOrchestrationContext : OrchestrationContext
    {
        public TrackingOrchestrationContext(string? version = null)
        {
            this.OrchestrationInstance = new()
            {
                InstanceId = Guid.NewGuid().ToString(),
                ExecutionId = Guid.NewGuid().ToString(),
            };
            this.Version = version ?? string.Empty;
        }

        public object? LastContinueAsNewInput { get; private set; }

        public string? LastContinueAsNewVersion { get; private set; }

        public string? LastScheduledTaskName { get; private set; }

        public string? LastScheduledTaskVersion { get; private set; }

        public object? LastScheduledTaskInput { get; private set; }

        public override void ContinueAsNew(object input)
        {
            this.LastContinueAsNewInput = input;
            this.LastContinueAsNewVersion = null;
        }

        public override void ContinueAsNew(string newVersion, object input)
        {
            this.LastContinueAsNewInput = input;
            this.LastContinueAsNewVersion = newVersion;
        }

        public override Task<T> CreateSubOrchestrationInstance<T>(string name, string version, object input)
            => throw new NotImplementedException();

        public override Task<T> CreateSubOrchestrationInstance<T>(string name, string version, string instanceId, object input)
            => throw new NotImplementedException();

        public override Task<T> CreateSubOrchestrationInstance<T>(string name, string version, string instanceId, object input, IDictionary<string, string> tags)
            => throw new NotImplementedException();

        public override Task<T> CreateTimer<T>(DateTime fireAt, T state)
            => throw new NotImplementedException();

        public override Task<T> CreateTimer<T>(DateTime fireAt, T state, CancellationToken cancelToken)
            => throw new NotImplementedException();

        public override Task<TResult> ScheduleTask<TResult>(string name, string version, params object[] parameters)
            => this.CaptureScheduledTask<TResult>(name, version, parameters);

        public override Task<TResult> ScheduleTask<TResult>(
            string name,
            string version,
            ScheduleTaskOptions options,
            params object[] parameters)
            => this.CaptureScheduledTask<TResult>(name, version, parameters);

        Task<TResult> CaptureScheduledTask<TResult>(string name, string version, object[] parameters)
        {
            this.LastScheduledTaskName = name;
            this.LastScheduledTaskVersion = version;
            this.LastScheduledTaskInput = parameters.Length switch
            {
                0 => null,
                1 => parameters[0],
                _ => parameters,
            };

            return Task.FromResult(default(TResult)!);
        }

        public override void SendEvent(OrchestrationInstance orchestrationInstance, string eventName, object eventData)
            => throw new NotImplementedException();
    }

    class TestOrchestrationContext : OrchestrationContext
    {
        public TestOrchestrationContext()
        {
            this.OrchestrationInstance = new()
            {
                InstanceId = Guid.NewGuid().ToString(),
                ExecutionId = Guid.NewGuid().ToString(),
            };
        }

        public override void ContinueAsNew(object input)
        {
            throw new NotImplementedException();
        }

        public override void ContinueAsNew(string newVersion, object input)
        {
            throw new NotImplementedException();
        }

        public override Task<T> CreateSubOrchestrationInstance<T>(string name, string version, object input)
        {
            throw new NotImplementedException();
        }

        public override Task<T> CreateSubOrchestrationInstance<T>(
            string name, string version, string instanceId, object input)
        {
            throw new NotImplementedException();
        }

        public override Task<T> CreateSubOrchestrationInstance<T>(
            string name, string version, string instanceId, object input, IDictionary<string, string> tags)
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

        public override Task<TResult> ScheduleTask<TResult>(string name, string version, params object[] parameters)
        {
            throw new NotImplementedException();
        }

        public override void SendEvent(OrchestrationInstance orchestrationInstance, string eventName, object eventData)
        {
            throw new NotImplementedException();
        }
    }
}
