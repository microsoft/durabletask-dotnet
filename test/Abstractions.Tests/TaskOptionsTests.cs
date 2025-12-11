// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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

        SubOrchestrationOptions subOptions = new();
        subOptions.Retry.Should().BeNull();
        subOptions.Tags.Should().BeNull();
        subOptions.InstanceId.Should().BeNull();

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
    public void ValidDedupeStatuses_ContainsExpectedTerminalStatuses()
    {
        // Act
#pragma warning disable CS0618 // Type or member is obsolete - Canceled is intentionally included for compatibility
        OrchestrationRuntimeStatus[] validStatuses = StartOrchestrationOptionsExtensions.ValidDedupeStatuses;

        // Assert
        validStatuses.Should().NotBeNull();
        validStatuses.Should().HaveCount(4);
        validStatuses.Should().Contain(OrchestrationRuntimeStatus.Completed);
        validStatuses.Should().Contain(OrchestrationRuntimeStatus.Failed);
        validStatuses.Should().Contain(OrchestrationRuntimeStatus.Terminated);
        validStatuses.Should().Contain(OrchestrationRuntimeStatus.Canceled);
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
}
