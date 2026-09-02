// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using DurableTask.Core;
using DurableTask.Core.Serializing.Internal;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.DurableTask.Worker.Shims;

public class TaskOrchestrationContextWrapperTests
{
    static readonly MethodInfo CompleteExternalEventMethod = typeof(TaskOrchestrationContextWrapper)
        .GetMethod(nameof(TaskOrchestrationContextWrapper.CompleteExternalEvent), BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"{nameof(TaskOrchestrationContextWrapper)}.{nameof(TaskOrchestrationContextWrapper.CompleteExternalEvent)} was not found.");

    static readonly FieldInfo CachedHashAlgorithmField = typeof(TaskOrchestrationContextWrapper)
        .GetField("cachedHashAlgorithm", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            $"{nameof(TaskOrchestrationContextWrapper)}.cachedHashAlgorithm was not found.");

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewGuid_FixedInputs_ProducesStableDeterministicValue(bool reuseNewGuidHashAlgorithm)
    {
        // Arrange — these golden values were computed independently (offline, using the documented
        // algorithm: SHA1("9e952958-5e33-4daf-827f-2fa12937b875" bytes + name bytes), with the RFC 4122
        // byte swaps and version/variant bits applied) for the given instance ID, timestamp, and counter.
        // This regression test protects replay compatibility: it must keep producing these exact GUIDs.
        TestOrchestrationContext innerContext = new(
            "fixed-instance-id",
            DateTime.Parse("2023-05-06T07:08:09.1234567Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        OrchestrationInvocationContext invocationContext = new(
            "Test",
            new(),
            NullLoggerFactory.Instance,
            null,
            reuseNewGuidHashAlgorithm);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        // Act
        Guid first = wrapper.NewGuid();
        Guid second = wrapper.NewGuid();
        Guid third = wrapper.NewGuid();

        // Assert
        first.Should().Be(Guid.Parse("0f353f85-75d2-56f8-89b5-a7773ace7605"));
        second.Should().Be(Guid.Parse("b0fd1465-f3d8-5a7e-98b1-f34137b15060"));
        third.Should().Be(Guid.Parse("12bec829-d5e1-563c-ac70-9806cad148c1"));
    }

    [Fact]
    public void NewGuid_DifferentInstanceId_ProducesDifferentStableDeterministicValue()
    {
        // Arrange — same timestamp and counter as the other golden-value test, but a different
        // instance ID, computed independently the same way. Confirms the instance ID is still part of
        // the hashed name and that the namespace/algorithm/byte-ordering were not altered.
        TestOrchestrationContext innerContext = new(
            "other-instance-id",
            DateTime.Parse("2023-05-06T07:08:09.1234567Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        OrchestrationInvocationContext invocationContext = new("Test", new(), NullLoggerFactory.Instance, null);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        // Act
        Guid result = wrapper.NewGuid();

        // Assert
        result.Should().Be(Guid.Parse("258d445c-0c1e-594c-a4a1-0a837e4ebe92"));
    }

    [Fact]
    public void NewGuid_CalledRepeatedly_ProducesDistinctValuesEachTime()
    {
        // Arrange — the internal counter advances on every call, so repeated calls with the same
        // instance ID and timestamp must still yield distinct GUIDs.
        TestOrchestrationContext innerContext = new(
            "repeat-instance-id",
            DateTime.Parse("2024-01-01T00:00:00.0000000Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        OrchestrationInvocationContext invocationContext = new("Test", new(), NullLoggerFactory.Instance, null);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        // Act
        List<Guid> results = new();
        for (int i = 0; i < 5; i++)
        {
            results.Add(wrapper.NewGuid());
        }

        // Assert — all five results are distinct from one another.
        results.Distinct().Should().HaveCount(5);
    }

    [Fact]
    public void NewGuid_ReplayingSameHistory_ProducesIdenticalGuidSequence()
    {
        // Arrange — simulates replay: two independent wrapper instances (as would be created for two
        // separate replay passes over the same orchestration history) observe the same instance ID and
        // the same sequence of CurrentUtcDateTime values as history is replayed.
        string instanceId = "replay-instance-id";
        DateTime timestamp = DateTime.Parse("2022-11-11T11:11:11.1111111Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        TestOrchestrationContext innerContext1 = new(instanceId, timestamp);
        OrchestrationInvocationContext invocationContext = new("Test", new(), NullLoggerFactory.Instance, null);
        TaskOrchestrationContextWrapper wrapper1 = new(innerContext1, invocationContext, "input");

        TestOrchestrationContext innerContext2 = new(instanceId, timestamp);
        TaskOrchestrationContextWrapper wrapper2 = new(innerContext2, invocationContext, "input");

        // Act — generate the same number of GUIDs from both "replay passes".
        Guid[] pass1 = [wrapper1.NewGuid(), wrapper1.NewGuid(), wrapper1.NewGuid()];
        Guid[] pass2 = [wrapper2.NewGuid(), wrapper2.NewGuid(), wrapper2.NewGuid()];

        // Assert — replay must produce an identical sequence of GUIDs given identical inputs.
        pass2.Should().Equal(pass1);
    }

    [Fact]
    public void NewGuid_MultipleCalls_ReuseCachedHashAlgorithmInstance()
    {
        // Arrange — verifies the optimization from
        // https://github.com/microsoft/durabletask-dotnet/issues/778: the SHA1 instance backing
        // NewGuid() is created once and reused across calls, rather than being constructed and
        // disposed on every call.
        TestOrchestrationContext innerContext = new();
        OrchestrationInvocationContext invocationContext = new(
            "Test",
            new(),
            NullLoggerFactory.Instance,
            null,
            ReuseNewGuidHashAlgorithm: true);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        // Act
        wrapper.NewGuid();
        object? afterFirstCall = CachedHashAlgorithmField.GetValue(wrapper);
        wrapper.NewGuid();
        object? afterSecondCall = CachedHashAlgorithmField.GetValue(wrapper);

        // Assert — the same underlying instance is reused rather than a new one being allocated.
        afterFirstCall.Should().NotBeNull();
        afterSecondCall.Should().BeSameAs(afterFirstCall);
    }

    [Fact]
    public void NewGuid_DefaultMode_DoesNotCacheHashAlgorithmInstance()
    {
        // Arrange
        TestOrchestrationContext innerContext = new();
        OrchestrationInvocationContext invocationContext = new(
            "Test",
            new(),
            NullLoggerFactory.Instance,
            null);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        // Act
        wrapper.NewGuid();
        object? afterFirstCall = CachedHashAlgorithmField.GetValue(wrapper);
        wrapper.NewGuid();
        object? afterSecondCall = CachedHashAlgorithmField.GetValue(wrapper);

        // Assert
        afterFirstCall.Should().BeNull();
        afterSecondCall.Should().BeNull();
    }

    [Fact]
    public void Dispose_ReleasesCachedHashAlgorithm()
    {
        // Arrange
        TestOrchestrationContext innerContext = new();
        OrchestrationInvocationContext invocationContext = new(
            "Test",
            new(),
            NullLoggerFactory.Instance,
            null,
            ReuseNewGuidHashAlgorithm: true);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        wrapper.NewGuid();
        SHA1 cachedInstance = (SHA1)(CachedHashAlgorithmField.GetValue(wrapper)
            ?? throw new InvalidOperationException("NewGuid() did not populate cachedHashAlgorithm."));

        // Act
        wrapper.Dispose();

        // Assert — the field is cleared, and the underlying instance was actually disposed (not merely
        // dereferenced), confirmed by it throwing when used afterwards.
        CachedHashAlgorithmField.GetValue(wrapper).Should().BeNull();
        Action useAfterDispose = () => cachedInstance.ComputeHash([1, 2, 3]);
        useAfterDispose.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        // Arrange
        TestOrchestrationContext innerContext = new();
        OrchestrationInvocationContext invocationContext = new(
            "Test",
            new(),
            NullLoggerFactory.Instance,
            null,
            ReuseNewGuidHashAlgorithm: true);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");
        wrapper.NewGuid();

        // Act
        Action dispose = () =>
        {
            wrapper.Dispose();
            wrapper.Dispose();
        };

        // Assert — disposing an already-disposed (or never-used) wrapper is safe.
        dispose.Should().NotThrow();
    }

    [Fact]
    public void Dispose_WithoutPriorNewGuidCall_DoesNotThrow()
    {
        // Arrange — the cached SHA1 instance is lazily created, so Dispose() must tolerate the case
        // where NewGuid() was never called.
        TestOrchestrationContext innerContext = new();
        OrchestrationInvocationContext invocationContext = new(
            "Test",
            new(),
            NullLoggerFactory.Instance,
            null,
            ReuseNewGuidHashAlgorithm: true);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        // Act
        Action dispose = () => wrapper.Dispose();

        // Assert
        dispose.Should().NotThrow();
    }

    [Fact]
    public void NewGuid_AfterDispose_StillProducesStableDeterministicValue()
    {
        // Arrange — Dispose() releases the cached SHA1 instance, but the wrapper lazily creates a new
        // one on the next NewGuid() call (via the `??=` pattern). This must still produce byte-identical
        // GUIDs to the ones computed with a fresh instance, proving disposal does not affect correctness.
        TestOrchestrationContext innerContext = new(
            "fixed-instance-id",
            DateTime.Parse("2023-05-06T07:08:09.1234567Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        OrchestrationInvocationContext invocationContext = new(
            "Test",
            new(),
            NullLoggerFactory.Instance,
            null,
            ReuseNewGuidHashAlgorithm: true);
        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, "input");

        // Act
        wrapper.Dispose(); // dispose before any use is allowed (no-op, since nothing was cached yet)
        Guid first = wrapper.NewGuid();
        wrapper.Dispose(); // dispose the now-cached instance mid-sequence
        Guid second = wrapper.NewGuid(); // should transparently create a new instance and continue correctly
        Guid third = wrapper.NewGuid();

        // Assert — identical to the golden values in NewGuid_FixedInputs_ProducesStableDeterministicValue.
        first.Should().Be(Guid.Parse("0f353f85-75d2-56f8-89b5-a7773ace7605"));
        second.Should().Be(Guid.Parse("b0fd1465-f3d8-5a7e-98b1-f34137b15060"));
        third.Should().Be(Guid.Parse("12bec829-d5e1-563c-ac70-9806cad148c1"));
    }

    static IReadOnlyDictionary<string, string> GetLastScheduledTaskTags(TrackingOrchestrationContext innerContext)
    {
        ScheduleTaskOptions options = innerContext.LastScheduledTaskOptions
            ?? throw new InvalidOperationException("No scheduled-task options were captured.");
        PropertyInfo tagsProperty = options.GetType().GetProperty("Tags")
            ?? throw new InvalidOperationException($"{options.GetType().FullName}.Tags was not found.");
        return tagsProperty.GetValue(options) as IReadOnlyDictionary<string, string>
            ?? throw new InvalidOperationException($"{options.GetType().FullName}.Tags was null or had an unexpected type.");
    }

    static void InvokeCompleteExternalEvent(TaskOrchestrationContextWrapper wrapper, string eventName, string rawEventPayload)
    {
        CompleteExternalEventMethod.Invoke(wrapper, [eventName, rawEventPayload]);
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

    sealed class TestOrchestrationContext : OrchestrationContext
    {
        // Only set when a fixed value is supplied via the constructor overload below; otherwise the
        // base class's (internally-set) value is used, preserving prior behavior for existing callers.
        readonly DateTime? fixedCurrentUtcDateTime;

        public TestOrchestrationContext()
            : this(Guid.NewGuid().ToString(), currentUtcDateTime: null)
        {
        }

        // Allows tests to pin the InstanceId and CurrentUtcDateTime that feed into NewGuid(), since
        // OrchestrationContext.CurrentUtcDateTime's setter is internal to DurableTask.Core and cannot
        // be assigned directly from this assembly.
        public TestOrchestrationContext(string instanceId, DateTime? currentUtcDateTime)
        {
            this.OrchestrationInstance = new()
            {
                InstanceId = instanceId,
                ExecutionId = Guid.NewGuid().ToString(),
            };
            this.fixedCurrentUtcDateTime = currentUtcDateTime;
        }

        public override DateTime CurrentUtcDateTime => this.fixedCurrentUtcDateTime ?? base.CurrentUtcDateTime;

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
