// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Reflection;
using DurableTask.Core;
using DurableTask.Core.Serializing.Internal;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.DurableTask.Worker.Shims;

public class TaskOrchestrationContextWrapperTests
{
    static readonly MethodInfo CompleteExternalEventMethod = typeof(TaskOrchestrationContextWrapper)
        .GetMethod(nameof(TaskOrchestrationContextWrapper.CompleteExternalEvent), BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"{nameof(TaskOrchestrationContextWrapper)}.{nameof(TaskOrchestrationContextWrapper.CompleteExternalEvent)} was not found.");

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
        innerContext.SentEvents[0].EventData.Should().BeOfType<RawInput>().Which.Value.Should().Be("\"payload\"");
        innerContext.LastContinueAsNewInput.Should().Be("new-input");
    }

    [Fact]
    public async Task CallActivityAsync_TaskOptionsVersionOverridesInheritedOrchestrationVersion()
    {
        // Arrange
        TrackingOrchestrationContext innerContext = new("v2");
        OrchestrationInvocationContext invocationContext = new("Test", new(), NullLoggerFactory.Instance, null);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        // Act
        await wrapper.CallActivityAsync<string>(
            "TestActivity",
            123,
            new TaskOptions
            {
                Version = "v1",
            });

        // Assert
        innerContext.LastScheduledTaskName.Should().Be("TestActivity");
        innerContext.LastScheduledTaskVersion.Should().Be("v1");
        innerContext.LastScheduledTaskInput.Should().Be(123);
    }

    [Fact]
    public async Task CallActivityAsync_TaskOptionsVersionOverridesInheritedOrchestrationVersion_WithRetryPolicy()
    {
        // Arrange
        TrackingOrchestrationContext innerContext = new("v2");
        OrchestrationInvocationContext invocationContext = new("Test", new(), NullLoggerFactory.Instance, null);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        // Act
        await wrapper.CallActivityAsync<string>(
            "TestActivity",
            123,
            new TaskOptions(new RetryPolicy(1, TimeSpan.FromSeconds(1)))
            {
                Version = "v1",
            });

        // Assert
        innerContext.LastScheduledTaskName.Should().Be("TestActivity");
        innerContext.LastScheduledTaskVersion.Should().Be("v1");
        innerContext.LastScheduledTaskInput.Should().Be(123);
    }

    [Fact]
    public async Task CallActivityAsync_TaskOptionsVersionOverridesInheritedOrchestrationVersion_WithRetryHandler()
    {
        // Arrange
        TrackingOrchestrationContext innerContext = new("v2");
        OrchestrationInvocationContext invocationContext = new("Test", new(), NullLoggerFactory.Instance, null);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");
        TaskOptions options = new(TaskOptions.FromRetryHandler(_ => false))
        {
            Version = "v1",
        };

        // Act
        await wrapper.CallActivityAsync<string>("TestActivity", 123, options);

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
    public async Task CallActivityAsync_PreservesCallerSuppliedTags()
    {
        // Arrange
        TrackingOrchestrationContext innerContext = new("v2");
        OrchestrationInvocationContext invocationContext = new("Test", new(), NullLoggerFactory.Instance, null);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        // Act — caller supplies arbitrary tags; the SDK preserves them verbatim.
        await wrapper.CallActivityAsync<string>(
            "TestActivity",
            123,
            new TaskOptions(tags: new Dictionary<string, string>
            {
                ["caller.tag"] = "caller-value",
            }));

        // Assert
        innerContext.LastScheduledTaskName.Should().Be("TestActivity");
        innerContext.LastScheduledTaskVersion.Should().Be("v2");
        innerContext.LastScheduledTaskInput.Should().Be(123);
        GetLastScheduledTaskTags(innerContext).Should().Contain("caller.tag", "caller-value");
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

    [Fact]
    public async Task CallActivityAsync_NullTaskOptionsVersion_InheritsOrchestrationVersion()
    {
        // Arrange — TaskOptions present but Version not set => inherit (same as plain TaskOptions).
        TrackingOrchestrationContext innerContext = new("v2");
        OrchestrationInvocationContext invocationContext = new("Test", new(), NullLoggerFactory.Instance, null);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        // Act
        await wrapper.CallActivityAsync<string>("TestActivity", 123, new TaskOptions());

        // Assert
        innerContext.LastScheduledTaskName.Should().Be("TestActivity");
        innerContext.LastScheduledTaskVersion.Should().Be("v2");
    }

    [Fact]
    public async Task CallActivityAsync_ExplicitUnversionedActivityOption_BypassesInherit()
    {
        // Arrange — from a v2 orchestration the caller explicitly requests the unversioned activity.
        TrackingOrchestrationContext innerContext = new("v2");
        OrchestrationInvocationContext invocationContext = new("Test", new(), NullLoggerFactory.Instance, null);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        // Act
        await wrapper.CallActivityAsync<string>(
            "TestActivity",
            123,
            new TaskOptions { Version = TaskVersion.Unversioned });

        // Assert — empty version is sent (the unversioned activity), instead of inheriting v2.
        innerContext.LastScheduledTaskName.Should().Be("TestActivity");
        innerContext.LastScheduledTaskVersion.Should().Be(string.Empty);
    }

    [Fact]
    public async Task CallSubOrchestratorAsync_PlainOptions_UsesWorkerDefaultVersion()
    {
        // Arrange — a sub-orchestration scheduled without an explicit Version uses the worker's
        // configured Versioning.DefaultVersion, mirroring the behavior the client gets when starting
        // a top-level orchestration. The parent's instance version is intentionally NOT inherited —
        // sub-orchestrations are new orchestration instances and follow the worker-default rule.
        TrackingOrchestrationContext innerContext = new("v2");
        OrchestrationInvocationContext invocationContext = new(
            "Test",
            new DurableTaskWorkerOptions
            {
                Versioning = new DurableTaskWorkerOptions.VersioningOptions { DefaultVersion = "9.9" },
            },
            NullLoggerFactory.Instance,
            null);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        // Act
        await wrapper.CallSubOrchestratorAsync<string>("ChildOrchestration", 123);

        // Assert
        innerContext.LastSubOrchestrationName.Should().Be("ChildOrchestration");
        innerContext.LastSubOrchestrationVersion.Should().Be("9.9");
    }

    [Fact]
    public async Task CallSubOrchestratorAsync_NoWorkerDefaultVersion_StampsEmptyVersion()
    {
        // Arrange — without a worker Versioning.DefaultVersion and without an explicit option, the
        // sub-orchestration is scheduled unversioned. The parent's own instance version is not
        // inherited; sub-orchestrations are new instances and follow the worker-default rule.
        TrackingOrchestrationContext innerContext = new("v2");
        OrchestrationInvocationContext invocationContext = new(
            "Test",
            new DurableTaskWorkerOptions(),
            NullLoggerFactory.Instance,
            null);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        // Act
        await wrapper.CallSubOrchestratorAsync<string>("ChildOrchestration", 123);

        // Assert
        innerContext.LastSubOrchestrationName.Should().Be("ChildOrchestration");
        innerContext.LastSubOrchestrationVersion.Should().Be(string.Empty);
    }

    [Fact]
    public async Task CallSubOrchestratorAsync_ExplicitVersion_OverridesWorkerDefaultVersion()
    {
        // Arrange — explicit SubOrchestrationOptions.Version wins over the worker's DefaultVersion.
        TrackingOrchestrationContext innerContext = new("v2");
        OrchestrationInvocationContext invocationContext = new(
            "Test",
            new DurableTaskWorkerOptions
            {
                Versioning = new DurableTaskWorkerOptions.VersioningOptions { DefaultVersion = "9.9" },
            },
            NullLoggerFactory.Instance,
            null);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        // Act
        await wrapper.CallSubOrchestratorAsync<string>(
            "ChildOrchestration",
            123,
            new SubOrchestrationOptions { Version = "v1" });

        // Assert
        innerContext.LastSubOrchestrationName.Should().Be("ChildOrchestration");
        innerContext.LastSubOrchestrationVersion.Should().Be("v1");
    }

    [Fact]
    public async Task CallSubOrchestratorAsync_ExplicitUnversionedOption_OverridesWorkerDefaultVersion()
    {
        // Arrange — explicit TaskVersion.Unversioned wins over the worker's DefaultVersion, producing
        // an unversioned sub-orchestration call.
        TrackingOrchestrationContext innerContext = new("v2");
        OrchestrationInvocationContext invocationContext = new(
            "Test",
            new DurableTaskWorkerOptions
            {
                Versioning = new DurableTaskWorkerOptions.VersioningOptions { DefaultVersion = "9.9" },
            },
            NullLoggerFactory.Instance,
            null);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        // Act
        await wrapper.CallSubOrchestratorAsync<string>(
            "ChildOrchestration",
            123,
            new SubOrchestrationOptions { Version = TaskVersion.Unversioned });

        // Assert
        innerContext.LastSubOrchestrationName.Should().Be("ChildOrchestration");
        innerContext.LastSubOrchestrationVersion.Should().Be(string.Empty);
    }

    [Fact]
    public void CompleteExternalEvent_CanceledWaiter_BuffersEventForNextWaiter()
    {
        // Arrange: reproduces the sequence from Azure/azure-functions-durable-extension#1676.
        // Two differently-named external event waits are registered with a shared cancellation
        // token, similar to a Task.WhenAny loop. The waiter for "event_1" is canceled (e.g.
        // because "event_0" won the race), but "event_1" is later raised while the canceled
        // waiter is still the only registered listener for that name.
        TrackingOrchestrationContext innerContext = new();
        OrchestrationInvocationContext invocationContext = new("Test", new(), NullLoggerFactory.Instance, null);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        using CancellationTokenSource cts = new();
        Task<string> canceledWait = wrapper.WaitForExternalEvent<string>("event_1", cts.Token);
        cts.Cancel();

        // Act: the event arrives after the only waiter for it has been canceled/abandoned.
        InvokeCompleteExternalEvent(wrapper, "event_1", "\"payload\"");

        // Assert: the canceled waiter must not "consume" the event. It should be buffered so
        // that a subsequent WaitForExternalEvent call (e.g. in the next ContinueAsNew generation)
        // can still observe it.
        canceledWait.IsCanceled.Should().BeTrue();
        Task<string> nextWait = wrapper.WaitForExternalEvent<string>("event_1");
        nextWait.IsCompletedSuccessfully.Should().BeTrue();
        nextWait.Result.Should().Be("payload");
    }

    [Fact]
    public void CompleteExternalEvent_CanceledWaiter_WithContinueAsNewPreserved_ForwardsEventToNextExecution()
    {
        // Arrange: same as above, but the event arrives after ContinueAsNew(preserveUnprocessedEvents: true)
        // has already been called (e.g. while an activity scheduled before ContinueAsNew is still
        // completing). The event should be forwarded to the next execution, not silently dropped.
        TrackingOrchestrationContext innerContext = new();
        OrchestrationInvocationContext invocationContext = new("Test", new(), NullLoggerFactory.Instance, null);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        using CancellationTokenSource cts = new();
        Task<string> canceledWait = wrapper.WaitForExternalEvent<string>("event_1", cts.Token);
        cts.Cancel();

        wrapper.ContinueAsNew("new-input", preserveUnprocessedEvents: true);

        // Act
        InvokeCompleteExternalEvent(wrapper, "event_1", "\"payload\"");

        // Assert
        canceledWait.IsCanceled.Should().BeTrue();
        innerContext.SentEvents.Should().ContainSingle();
        innerContext.SentEvents[0].InstanceId.Should().Be(wrapper.InstanceId);
        innerContext.SentEvents[0].EventName.Should().Be("event_1");
        innerContext.SentEvents[0].EventData.Should().BeOfType<RawInput>().Which.Value.Should().Be("\"payload\"");
    }

    [Fact]
    public void CompleteExternalEvent_MultipleCanceledWaiters_BuffersEventAfterSkippingAll()
    {
        // Arrange: several waiters for the same event name are canceled/abandoned in a row
        // (e.g. a loop that repeatedly races and abandons the same event). None of them should
        // be able to consume the event.
        TrackingOrchestrationContext innerContext = new();
        CountingDataConverter converter = new();
        OrchestrationInvocationContext invocationContext = new(
            "Test",
            new DurableTaskWorkerOptions { DataConverter = converter },
            NullLoggerFactory.Instance,
            null);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        using CancellationTokenSource cts1 = new();
        using CancellationTokenSource cts2 = new();
        using CancellationTokenSource cts3 = new();
        Task<string> wait1 = wrapper.WaitForExternalEvent<string>("event_1", cts1.Token);
        Task<string> wait2 = wrapper.WaitForExternalEvent<string>("event_1", cts2.Token);
        Task<string> wait3 = wrapper.WaitForExternalEvent<string>("event_1", cts3.Token);
        cts1.Cancel();
        cts2.Cancel();
        cts3.Cancel();

        // Act
        InvokeCompleteExternalEvent(wrapper, "event_1", "\"payload\"");

        // Assert
        converter.DeserializeCallCounts[typeof(string)].Should().Be(1);
        wait1.IsCanceled.Should().BeTrue();
        wait2.IsCanceled.Should().BeTrue();
        wait3.IsCanceled.Should().BeTrue();
        Task<string> nextWait = wrapper.WaitForExternalEvent<string>("event_1");
        nextWait.IsCompletedSuccessfully.Should().BeTrue();
        nextWait.Result.Should().Be("payload");
    }

    [Fact]
    public void CompleteExternalEvent_ActiveWaiterAboveCanceledWaiter_DeliversToActiveWaiter()
    {
        // Arrange: the most recent (top-of-stack) waiter is still live, but an older waiter
        // underneath it for the same event name was already canceled. The event should go to
        // the live waiter, and the canceled one underneath should remain untouched (not consumed).
        TrackingOrchestrationContext innerContext = new();
        OrchestrationInvocationContext invocationContext = new("Test", new(), NullLoggerFactory.Instance, null);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        using CancellationTokenSource cts1 = new();
        Task<string> canceledWait = wrapper.WaitForExternalEvent<string>("event_1", cts1.Token);
        cts1.Cancel();
        Task<string> activeWait = wrapper.WaitForExternalEvent<string>("event_1");

        // Act
        InvokeCompleteExternalEvent(wrapper, "event_1", "\"payload\"");

        // Assert
        canceledWait.IsCanceled.Should().BeTrue();
        activeWait.IsCompletedSuccessfully.Should().BeTrue();
        activeWait.Result.Should().Be("payload");
    }

    static IReadOnlyDictionary<string, string> GetLastScheduledTaskTags(TrackingOrchestrationContext innerContext)
    {
        PropertyInfo tagsProperty = innerContext.LastScheduledTaskOptions!.GetType().GetProperty("Tags")!;
        return (IReadOnlyDictionary<string, string>)tagsProperty.GetValue(innerContext.LastScheduledTaskOptions)!;
    }

    static void InvokeCompleteExternalEvent(TaskOrchestrationContextWrapper wrapper, string eventName, string rawEventPayload)
    {
        CompleteExternalEventMethod.Invoke(wrapper, [eventName, rawEventPayload]);
    }

    sealed class CountingDataConverter : DataConverter
    {
        public Dictionary<Type, int> DeserializeCallCounts { get; } = [];

        public override object? Deserialize(string? data, Type targetType)
        {
            this.DeserializeCallCounts[targetType] = this.DeserializeCallCounts.GetValueOrDefault(targetType) + 1;
            return targetType == typeof(string) ? "payload" : null;
        }

        public override string? Serialize(object? value) => throw new NotSupportedException();
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

        public ScheduleTaskOptions? LastScheduledTaskOptions { get; private set; }

        public string? LastSubOrchestrationName { get; private set; }

        public string? LastSubOrchestrationVersion { get; private set; }

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
        {
            this.LastSubOrchestrationName = name;
            this.LastSubOrchestrationVersion = version;
            return Task.FromResult(default(T)!);
        }

        public override Task<T> CreateSubOrchestrationInstance<T>(string name, string version, string instanceId, object input)
        {
            this.LastSubOrchestrationName = name;
            this.LastSubOrchestrationVersion = version;
            return Task.FromResult(default(T)!);
        }

        public override Task<T> CreateSubOrchestrationInstance<T>(string name, string version, string instanceId, object input, IDictionary<string, string> tags)
        {
            this.LastSubOrchestrationName = name;
            this.LastSubOrchestrationVersion = version;
            return Task.FromResult(default(T)!);
        }

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
            => this.CaptureScheduledTask<TResult>(name, version, parameters, options);

        Task<TResult> CaptureScheduledTask<TResult>(
            string name,
            string version,
            object[] parameters,
            ScheduleTaskOptions? options = null)
        {
            this.LastScheduledTaskName = name;
            this.LastScheduledTaskVersion = version;
            this.LastScheduledTaskInput = parameters.Length switch
            {
                0 => null,
                1 => parameters[0],
                _ => parameters,
            };
            this.LastScheduledTaskOptions = options;

            return Task.FromResult(default(TResult)!);
        }

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
