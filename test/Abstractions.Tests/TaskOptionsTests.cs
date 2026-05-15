// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Microsoft.DurableTask.Client;

namespace Microsoft.DurableTask.Tests;

public class TaskOptionsTests
{
    [Fact]
    public void Empty_Ctors_Okay()
    {
        TaskOptions options = new();
        options.Retry.Should().BeNull();
        options.Tags.Should().BeNull();
        options.Version.Should().BeNull();

        SubOrchestrationOptions subOptions = new();
        subOptions.Retry.Should().BeNull();
        subOptions.Tags.Should().BeNull();
        subOptions.InstanceId.Should().BeNull();
        subOptions.Version.Should().BeNull();

        StartOrchestrationOptions startOptions = new();
        startOptions.Version.Should().BeNull();
        startOptions.InstanceId.Should().BeNull();
        startOptions.StartAt.Should().BeNull();
        startOptions.Tags.Should().BeEmpty();
    }

    [Fact]
    public void SubOrchestrationOptions_InstanceId_Correct()
    {
        string instanceId = Guid.NewGuid().ToString();
        SubOrchestrationOptions subOptions = new(new TaskOptions(), instanceId);
        instanceId.Equals(subOptions.InstanceId).Should().BeTrue();

        string subInstanceId = Guid.NewGuid().ToString();
        subOptions = new(new SubOrchestrationOptions(instanceId: subInstanceId));
        subInstanceId.Equals(subOptions.InstanceId).Should().BeTrue();

        subOptions = new(new SubOrchestrationOptions(instanceId: subInstanceId), instanceId);
        instanceId.Equals(subOptions.InstanceId).Should().BeTrue();
    }

    [Fact]
    public void WithDedupeStatuses_SetsCorrectStringValues()
    {
        // Arrange
        StartOrchestrationOptions options = new();
        OrchestrationRuntimeStatus[] statuses = new[]
        {
            OrchestrationRuntimeStatus.Completed,
            OrchestrationRuntimeStatus.Failed,
            OrchestrationRuntimeStatus.Terminated,
        };

        // Act
        StartOrchestrationOptions result = options.WithDedupeStatuses(statuses);

        // Assert
        result.DedupeStatuses.Should().NotBeNull();
        result.DedupeStatuses.Should().HaveCount(3);
        result.DedupeStatuses.Should().Contain("Completed");
        result.DedupeStatuses.Should().Contain("Failed");
        result.DedupeStatuses.Should().Contain("Terminated");
    }

    [Fact]
    public void WithDedupeStatuses_HandlesEmptyArray()
    {
        // Arrange
        StartOrchestrationOptions options = new();

        // Act
        StartOrchestrationOptions result = options.WithDedupeStatuses();

        // Assert
        result.DedupeStatuses.Should().NotBeNull();
        result.DedupeStatuses.Should().BeEmpty();
    }

    [Fact]
    public void WithDedupeStatuses_HandlesEmptyArrayExplicit()
    {
        // Arrange
        StartOrchestrationOptions options = new();
        OrchestrationRuntimeStatus[] statuses = Array.Empty<OrchestrationRuntimeStatus>();

        // Act
        StartOrchestrationOptions result = options.WithDedupeStatuses(statuses);

        // Assert
        result.DedupeStatuses.Should().NotBeNull();
        result.DedupeStatuses.Should().BeEmpty();
    }

    [Fact]
    public void WithDedupeStatuses_PreservesOtherProperties()
    {
        // Arrange
        string instanceId = Guid.NewGuid().ToString();
        DateTimeOffset startAt = DateTimeOffset.UtcNow.AddHours(1);
        StartOrchestrationOptions options = new(instanceId, startAt);

        // Act
        StartOrchestrationOptions result = options.WithDedupeStatuses(
            OrchestrationRuntimeStatus.Completed,
            OrchestrationRuntimeStatus.Failed);

        // Assert
        result.InstanceId.Should().Be(instanceId);
        result.StartAt.Should().Be(startAt);
        result.DedupeStatuses.Should().NotBeNull();
        result.DedupeStatuses.Should().HaveCount(2);
    }

    [Fact]
    public void ValidDedupeStatuses_ContainsExpectedStatuses()
    {
        // Act
#pragma warning disable CS0618 // Type or member is obsolete - Canceled is intentionally included for compatibility
        IReadOnlyList<OrchestrationRuntimeStatus> validStatuses = StartOrchestrationOptionsExtensions.ValidDedupeStatuses;

        // Assert
        validStatuses.Should().NotBeNull();
        validStatuses.Should().HaveCount(7);
        validStatuses.Should().Contain(OrchestrationRuntimeStatus.Completed);
        validStatuses.Should().Contain(OrchestrationRuntimeStatus.Failed);
        validStatuses.Should().Contain(OrchestrationRuntimeStatus.Terminated);
        validStatuses.Should().Contain(OrchestrationRuntimeStatus.Canceled);
        validStatuses.Should().Contain(OrchestrationRuntimeStatus.Pending);
        validStatuses.Should().Contain(OrchestrationRuntimeStatus.Running);
        validStatuses.Should().Contain(OrchestrationRuntimeStatus.Suspended);

#pragma warning restore CS0618
    }

    [Fact]
    public void WithDedupeStatuses_ConvertsAllEnumValuesToStrings()
    {
        // Arrange
        StartOrchestrationOptions options = new();
        OrchestrationRuntimeStatus[] allStatuses = Enum.GetValues<OrchestrationRuntimeStatus>();

        // Act
        StartOrchestrationOptions result = options.WithDedupeStatuses(allStatuses);

        // Assert
        result.DedupeStatuses.Should().NotBeNull();
        result.DedupeStatuses.Should().HaveCount(allStatuses.Length);
        foreach (OrchestrationRuntimeStatus status in allStatuses)
        {
            result.DedupeStatuses.Should().Contain(status.ToString());
        }
    }

    [Fact]
    public void TaskOptions_CopyConstructor_CopiesAllProperties()
    {
        // Arrange
        RetryPolicy policy = new(3, TimeSpan.FromSeconds(1));
        TaskRetryOptions retry = new(policy);
        Dictionary<string, string> tags = new() { { "key1", "value1" }, { "key2", "value2" } };
        TaskOptions original = new(retry, tags);

        // Act
        TaskOptions copy = new(original);

        // Assert
        copy.Retry.Should().Be(original.Retry);
        copy.Tags.Should().BeSameAs(original.Tags);
    }

    [Fact]
    public void TaskOptions_VersionInitializer_PersistsValue()
    {
        // Arrange
        TaskVersion version = new("1.0");

        // Act
        TaskOptions options = new() { Version = version };

        // Assert
        options.Version.Should().Be(version);
    }

    [Fact]
    public void TaskOptions_CopyConstructor_CopiesAllPropertiesIncludingVersion()
    {
        // Arrange
        RetryPolicy policy = new(3, TimeSpan.FromSeconds(1));
        TaskRetryOptions retry = new(policy);
        Dictionary<string, string> tags = new() { { "key1", "value1" }, { "key2", "value2" } };
        TaskVersion version = new("1.0");
        TaskOptions original = new(retry, tags)
        {
            Version = version,
        };

        // Act
        TaskOptions copy = new(original);

        // Assert
        copy.Retry.Should().Be(original.Retry);
        copy.Tags.Should().BeSameAs(original.Tags);
        copy.Version.Should().Be(original.Version);
    }

    [Fact]
    public void SubOrchestrationOptions_CopyConstructor_CopiesAllProperties()
    {
        // Arrange
        RetryPolicy policy = new(3, TimeSpan.FromSeconds(1));
        TaskRetryOptions retry = new(policy);
        Dictionary<string, string> tags = new() { { "key1", "value1" }, { "key2", "value2" } };
        string instanceId = Guid.NewGuid().ToString();
        TaskVersion version = new("1.0");
        SubOrchestrationOptions original = new(retry, instanceId)
        {
            Tags = tags,
            Version = version,
        };

        // Act
        SubOrchestrationOptions copy = new(original);

        // Assert
        copy.Retry.Should().Be(original.Retry);
        copy.Tags.Should().BeSameAs(original.Tags);
        copy.InstanceId.Should().Be(original.InstanceId);
        copy.Version.Should().Be(original.Version);
    }

    [Fact]
    public void SubOrchestrationOptions_CopyFromTaskOptions_CopiesVersionWhenSourceIsSubOrchestration()
    {
        // Arrange
        RetryPolicy policy = new(3, TimeSpan.FromSeconds(1));
        TaskRetryOptions retry = new(policy);
        Dictionary<string, string> tags = new() { { "key1", "value1" } };
        string instanceId = Guid.NewGuid().ToString();
        TaskVersion version = new("1.0");
        SubOrchestrationOptions original = new(retry, instanceId)
        {
            Tags = tags,
            Version = version,
        };

        // Act
        SubOrchestrationOptions copy = new(original as TaskOptions);

        // Assert
        copy.Retry.Should().Be(original.Retry);
        copy.Tags.Should().BeSameAs(original.Tags);
        copy.InstanceId.Should().Be(original.InstanceId);
        copy.Version.Should().Be(original.Version);
    }

    [Fact]
    public void StartOrchestrationOptions_CopyConstructor_CopiesAllProperties()
    {
        // Arrange
        string instanceId = Guid.NewGuid().ToString();
        DateTimeOffset startAt = DateTimeOffset.UtcNow.AddHours(1);
        Dictionary<string, string> tags = new() { { "key1", "value1" }, { "key2", "value2" } };
        TaskVersion version = new("1.0");
        List<string> dedupeStatuses = new() { "Completed", "Failed" };
        StartOrchestrationOptions original = new(instanceId, startAt)
        {
            Tags = tags,
            Version = version,
            DedupeStatuses = dedupeStatuses,
        };

        // Act
        StartOrchestrationOptions copy = new(original);

        // Assert
        copy.InstanceId.Should().Be(original.InstanceId);
        copy.StartAt.Should().Be(original.StartAt);
        copy.Tags.Should().BeSameAs(original.Tags);
        copy.Version.Should().Be(original.Version);
        copy.DedupeStatuses.Should().BeSameAs(original.DedupeStatuses);
    }

    [Fact]
    public void SubOrchestrationOptions_VersionPropertyDeclaredOnDerived_PreservesBinaryCompat()
    {
        // Pins that SubOrchestrationOptions.Version is declared (with `new`) on the derived
        // record so the IL symbol `SubOrchestrationOptions.get_Version()` continues to resolve
        // for assemblies compiled against earlier SDK versions that declared Version directly on
        // SubOrchestrationOptions.
        PropertyInfo? versionProp = typeof(SubOrchestrationOptions).GetProperty(
            nameof(SubOrchestrationOptions.Version),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        versionProp.Should().NotBeNull(
            "SubOrchestrationOptions.Version must remain declared on the derived type for binary compatibility " +
            "with assemblies compiled against earlier SDK versions.");
        versionProp!.DeclaringType.Should().Be(typeof(SubOrchestrationOptions));
        versionProp.PropertyType.Should().Be(typeof(TaskVersion?));
    }

    [Fact]
    public void SubOrchestrationOptions_VersionGetterAndSetter_ForwardToBaseTaskOptions()
    {
        // Setting through SubOrchestrationOptions.Version must round-trip through TaskOptions.Version
        // (forwarding accessors). Both must observe the same value with no duplicate backing store.
        SubOrchestrationOptions sub = new() { Version = new TaskVersion("v1") };

        sub.Version.Should().Be(new TaskVersion("v1"));
        ((TaskOptions)sub).Version.Should().Be(new TaskVersion("v1"));
    }
}
