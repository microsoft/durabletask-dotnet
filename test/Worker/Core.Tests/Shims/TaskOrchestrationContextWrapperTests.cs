// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using DurableTask.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.DurableTask.Worker.Shims;

public class TaskOrchestrationContextWrapperTests
{
    static readonly MethodInfo CompleteExternalEventMethod = typeof(TaskOrchestrationContextWrapper)
        .GetMethod("CompleteExternalEvent", BindingFlags.Instance | BindingFlags.NonPublic)!;

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
    public void ContinueAsNew_WithPreserveUnprocessedEvents_ForwardsLateArrivingEventsToNextExecution()
    {
        // Arrange
        TrackingOrchestrationContext innerContext = new();
        OrchestrationInvocationContext invocationContext = new("Test", new(), NullLoggerFactory.Instance, null);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        // Act
        wrapper.ContinueAsNew("new-input", preserveUnprocessedEvents: true);
        InvokeCompleteExternalEvent(wrapper, "Event", "\"payload\"");

        // Assert
        innerContext.SentEvents.Should().ContainSingle();
        innerContext.SentEvents[0].InstanceId.Should().Be(wrapper.InstanceId);
        innerContext.SentEvents[0].EventName.Should().Be("Event");
        innerContext.LastContinueAsNewInput.Should().Be("new-input");
    }

    static void InvokeCompleteExternalEvent(TaskOrchestrationContextWrapper wrapper, string eventName, string rawEventPayload)
    {
        CompleteExternalEventMethod.Invoke(wrapper, [eventName, rawEventPayload]);
    }

    sealed class TrackingOrchestrationContext : OrchestrationContext
    {
        public TrackingOrchestrationContext()
        {
            this.OrchestrationInstance = new()
            {
                InstanceId = Guid.NewGuid().ToString(),
                ExecutionId = Guid.NewGuid().ToString(),
            };
        }

        public object? LastContinueAsNewInput { get; private set; }

        public string? LastContinueAsNewVersion { get; private set; }

        public List<(string InstanceId, string EventName, object EventData)> SentEvents { get; } = [];

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
            => throw new NotImplementedException();

        public override void SendEvent(OrchestrationInstance orchestrationInstance, string eventName, object eventData)
        {
            this.SentEvents.Add((orchestrationInstance.InstanceId, eventName, eventData));
        }
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
