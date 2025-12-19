// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Client.Grpc.Tests;

public class ProtoUtilsTests
{
    [Fact]
    public void GetTerminalStatuses_ReturnsExpectedStatuses()
    {
        // Act
        ImmutableArray<P.OrchestrationStatus> terminalStatuses = ProtoUtils.GetTerminalStatuses();

        // Assert
        terminalStatuses.Should().HaveCount(4);
        terminalStatuses.Should().Contain(P.OrchestrationStatus.Completed);
        terminalStatuses.Should().Contain(P.OrchestrationStatus.Failed);
        terminalStatuses.Should().Contain(P.OrchestrationStatus.Terminated);
#pragma warning disable CS0618 // Type or member is obsolete
        terminalStatuses.Should().Contain(P.OrchestrationStatus.Canceled);
#pragma warning restore CS0618
    }

    [Fact]
    public void GetTerminalStatuses_ReturnsImmutableArray()
    {
        // Act
        ImmutableArray<P.OrchestrationStatus> terminalStatuses = ProtoUtils.GetTerminalStatuses();

        // Assert
        terminalStatuses.IsDefault.Should().BeFalse();
        terminalStatuses.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void ConvertDedupeStatusesToReusePolicy_EmptyArray_ReturnsPolicyWithAllTerminalStatuses()
    {
        // Arrange
        var dedupeStatuses = Array.Empty<P.OrchestrationStatus>();

        // Act
        P.OrchestrationIdReusePolicy? result = ProtoUtils.ConvertDedupeStatusesToReusePolicy(dedupeStatuses);

        // Assert
        // Empty array means no dedupe statuses, so all terminal statuses are replaceable
        result.Should().NotBeNull();
        result!.ReplaceableStatus.Should().HaveCount(4);
    }

    [Fact]
    public void ConvertDedupeStatusesToReusePolicy_AllTerminalStatuses_ReturnsNull()
    {
        // Arrange
        ImmutableArray<P.OrchestrationStatus> allTerminalStatuses = ProtoUtils.GetTerminalStatuses();
        var dedupeStatuses = allTerminalStatuses.ToArray();

        // Act
        P.OrchestrationIdReusePolicy? result = ProtoUtils.ConvertDedupeStatusesToReusePolicy(dedupeStatuses);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ConvertDedupeStatusesToReusePolicy_NoDedupeStatuses_ReturnsPolicyWithAllTerminalStatuses()
    {
        // Arrange
        var dedupeStatuses = Array.Empty<P.OrchestrationStatus>();

        // Act
        P.OrchestrationIdReusePolicy? result = ProtoUtils.ConvertDedupeStatusesToReusePolicy(dedupeStatuses);

        // Assert
        // When no dedupe statuses, all terminal statuses should be replaceable
        result.Should().NotBeNull();
        result!.ReplaceableStatus.Should().HaveCount(4);
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Completed);
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Failed);
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Terminated);
#pragma warning disable CS0618 // Type or member is obsolete
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Canceled);
#pragma warning restore CS0618
    }

    [Fact]
    public void ConvertDedupeStatusesToReusePolicy_SingleDedupeStatus_ReturnsPolicyWithRemainingStatuses()
    {
        // Arrange
        var dedupeStatuses = new[] { P.OrchestrationStatus.Completed };

        // Act
        P.OrchestrationIdReusePolicy? result = ProtoUtils.ConvertDedupeStatusesToReusePolicy(dedupeStatuses);

        // Assert
        result.Should().NotBeNull();
        result!.ReplaceableStatus.Should().HaveCount(3);
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Failed);
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Terminated);
#pragma warning disable CS0618 // Type or member is obsolete
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Canceled);
#pragma warning restore CS0618
        result.ReplaceableStatus.Should().NotContain(P.OrchestrationStatus.Completed);
    }

    [Fact]
    public void ConvertDedupeStatusesToReusePolicy_MultipleDedupeStatuses_ReturnsPolicyWithRemainingStatuses()
    {
        // Arrange
        var dedupeStatuses = new[] 
        { 
            P.OrchestrationStatus.Completed, 
            P.OrchestrationStatus.Failed 
        };

        // Act
        P.OrchestrationIdReusePolicy? result = ProtoUtils.ConvertDedupeStatusesToReusePolicy(dedupeStatuses);

        // Assert
        result.Should().NotBeNull();
        result!.ReplaceableStatus.Should().HaveCount(2);
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Terminated);
#pragma warning disable CS0618 // Type or member is obsolete
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Canceled);
#pragma warning restore CS0618
        result.ReplaceableStatus.Should().NotContain(P.OrchestrationStatus.Completed);
        result.ReplaceableStatus.Should().NotContain(P.OrchestrationStatus.Failed);
    }

    [Fact]
    public void ConvertDedupeStatusesToReusePolicy_DuplicateDedupeStatuses_HandlesDuplicates()
    {
        // Arrange
        var dedupeStatuses = new[] 
        { 
            P.OrchestrationStatus.Completed, 
            P.OrchestrationStatus.Completed,
            P.OrchestrationStatus.Failed
        };

        // Act
        P.OrchestrationIdReusePolicy? result = ProtoUtils.ConvertDedupeStatusesToReusePolicy(dedupeStatuses);

        // Assert
        result.Should().NotBeNull();
        result!.ReplaceableStatus.Should().HaveCount(2);
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Terminated);
#pragma warning disable CS0618 // Type or member is obsolete
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Canceled);
#pragma warning restore CS0618
    }

    [Fact]
    public void ConvertDedupeStatusesToReusePolicy_NonTerminalStatus_IgnoresNonTerminalStatus()
    {
        // Arrange
        var dedupeStatuses = new[] 
        { 
            P.OrchestrationStatus.Completed,
            P.OrchestrationStatus.Running, // Non-terminal status
            P.OrchestrationStatus.Pending  // Non-terminal status
        };

        // Act
        P.OrchestrationIdReusePolicy? result = ProtoUtils.ConvertDedupeStatusesToReusePolicy(dedupeStatuses);

        // Assert
        result.Should().NotBeNull();
        result!.ReplaceableStatus.Should().HaveCount(3);
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Failed);
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Terminated);
#pragma warning disable CS0618 // Type or member is obsolete
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Canceled);
#pragma warning restore CS0618
        result.ReplaceableStatus.Should().NotContain(P.OrchestrationStatus.Completed);
    }

    [Fact]
    public void ConvertReusePolicyToDedupeStatuses_NullPolicy_ReturnsNull()
    {
        // Arrange
        P.OrchestrationIdReusePolicy? policy = null;

        // Act
        P.OrchestrationStatus[]? result = ProtoUtils.ConvertReusePolicyToDedupeStatuses(policy);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ConvertReusePolicyToDedupeStatuses_EmptyPolicy_ReturnsNull()
    {
        // Arrange
        var policy = new P.OrchestrationIdReusePolicy();

        // Act
        P.OrchestrationStatus[]? result = ProtoUtils.ConvertReusePolicyToDedupeStatuses(policy);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ConvertReusePolicyToDedupeStatuses_AllTerminalStatusesReplaceable_ReturnsNull()
    {
        // Arrange
        var policy = new P.OrchestrationIdReusePolicy();
        ImmutableArray<P.OrchestrationStatus> terminalStatuses = ProtoUtils.GetTerminalStatuses();
        foreach (var status in terminalStatuses)
        {
            policy.ReplaceableStatus.Add(status);
        }

        // Act
        P.OrchestrationStatus[]? result = ProtoUtils.ConvertReusePolicyToDedupeStatuses(policy);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ConvertReusePolicyToDedupeStatuses_SingleReplaceableStatus_ReturnsRemainingStatuses()
    {
        // Arrange
        var policy = new P.OrchestrationIdReusePolicy();
        policy.ReplaceableStatus.Add(P.OrchestrationStatus.Completed);

        // Act
        P.OrchestrationStatus[]? result = ProtoUtils.ConvertReusePolicyToDedupeStatuses(policy);

        // Assert
        result.Should().NotBeNull();
        result!.Should().HaveCount(3);
        result.Should().Contain(P.OrchestrationStatus.Failed);
        result.Should().Contain(P.OrchestrationStatus.Terminated);
#pragma warning disable CS0618 // Type or member is obsolete
        result.Should().Contain(P.OrchestrationStatus.Canceled);
#pragma warning restore CS0618
        result.Should().NotContain(P.OrchestrationStatus.Completed);
    }

    [Fact]
    public void ConvertReusePolicyToDedupeStatuses_MultipleReplaceableStatuses_ReturnsRemainingStatuses()
    {
        // Arrange
        var policy = new P.OrchestrationIdReusePolicy();
        policy.ReplaceableStatus.Add(P.OrchestrationStatus.Completed);
        policy.ReplaceableStatus.Add(P.OrchestrationStatus.Failed);

        // Act
        P.OrchestrationStatus[]? result = ProtoUtils.ConvertReusePolicyToDedupeStatuses(policy);

        // Assert
        result.Should().NotBeNull();
        result!.Should().HaveCount(2);
        result.Should().Contain(P.OrchestrationStatus.Terminated);
#pragma warning disable CS0618 // Type or member is obsolete
        result.Should().Contain(P.OrchestrationStatus.Canceled);
#pragma warning restore CS0618
        result.Should().NotContain(P.OrchestrationStatus.Completed);
        result.Should().NotContain(P.OrchestrationStatus.Failed);
    }

    [Fact]
    public void ConvertReusePolicyToDedupeStatuses_NonTerminalStatusInPolicy_IgnoresNonTerminalStatus()
    {
        // Arrange
        var policy = new P.OrchestrationIdReusePolicy();
        policy.ReplaceableStatus.Add(P.OrchestrationStatus.Completed);
        policy.ReplaceableStatus.Add(P.OrchestrationStatus.Running); // Non-terminal status
        policy.ReplaceableStatus.Add(P.OrchestrationStatus.Pending); // Non-terminal status

        // Act
        P.OrchestrationStatus[]? result = ProtoUtils.ConvertReusePolicyToDedupeStatuses(policy);

        // Assert
        result.Should().NotBeNull();
        result!.Should().HaveCount(3);
        result.Should().Contain(P.OrchestrationStatus.Failed);
        result.Should().Contain(P.OrchestrationStatus.Terminated);
#pragma warning disable CS0618 // Type or member is obsolete
        result.Should().Contain(P.OrchestrationStatus.Canceled);
#pragma warning restore CS0618
        result.Should().NotContain(P.OrchestrationStatus.Completed);
    }

    [Fact]
    public void ConvertReusePolicyToDedupeStatuses_DuplicateReplaceableStatuses_HandlesDuplicates()
    {
        // Arrange
        var policy = new P.OrchestrationIdReusePolicy();
        policy.ReplaceableStatus.Add(P.OrchestrationStatus.Completed);
        policy.ReplaceableStatus.Add(P.OrchestrationStatus.Completed); // Duplicate
        policy.ReplaceableStatus.Add(P.OrchestrationStatus.Failed);

        // Act
        P.OrchestrationStatus[]? result = ProtoUtils.ConvertReusePolicyToDedupeStatuses(policy);

        // Assert
        result.Should().NotBeNull();
        result!.Should().HaveCount(2);
        result.Should().Contain(P.OrchestrationStatus.Terminated);
#pragma warning disable CS0618 // Type or member is obsolete
        result.Should().Contain(P.OrchestrationStatus.Canceled);
#pragma warning restore CS0618
    }

    [Fact]
    public void ConvertDedupeStatusesToReusePolicy_ThenConvertBack_ReturnsOriginalDedupeStatuses()
    {
        // Arrange
        var originalDedupeStatuses = new[] 
        { 
            P.OrchestrationStatus.Completed, 
            P.OrchestrationStatus.Failed 
        };

        // Act
        P.OrchestrationIdReusePolicy? policy = ProtoUtils.ConvertDedupeStatusesToReusePolicy(originalDedupeStatuses);
        P.OrchestrationStatus[]? convertedBack = ProtoUtils.ConvertReusePolicyToDedupeStatuses(policy);

        // Assert
        convertedBack.Should().NotBeNull();
        convertedBack!.Should().BeEquivalentTo(originalDedupeStatuses);
    }

    [Fact]
    public void ConvertReusePolicyToDedupeStatuses_ThenConvertBack_ReturnsOriginalPolicy()
    {
        // Arrange
        var policy = new P.OrchestrationIdReusePolicy();
        policy.ReplaceableStatus.Add(P.OrchestrationStatus.Completed);
        policy.ReplaceableStatus.Add(P.OrchestrationStatus.Failed);

        // Act
        P.OrchestrationStatus[]? dedupeStatuses = ProtoUtils.ConvertReusePolicyToDedupeStatuses(policy);
        P.OrchestrationIdReusePolicy? convertedBack = ProtoUtils.ConvertDedupeStatusesToReusePolicy(dedupeStatuses);

        // Assert
        convertedBack.Should().NotBeNull();
        convertedBack!.ReplaceableStatus.Should().BeEquivalentTo(policy.ReplaceableStatus);
    }

    [Fact]
    public void ConvertDedupeStatusesToReusePolicy_AllStatuses_ThenConvertBack_ReturnsNull()
    {
        // Arrange
        ImmutableArray<P.OrchestrationStatus> allTerminalStatuses = ProtoUtils.GetTerminalStatuses();
        var dedupeStatuses = allTerminalStatuses.ToArray();

        // Act
        P.OrchestrationIdReusePolicy? policy = ProtoUtils.ConvertDedupeStatusesToReusePolicy(dedupeStatuses);
        P.OrchestrationStatus[]? convertedBack = ProtoUtils.ConvertReusePolicyToDedupeStatuses(policy);

        // Assert
        policy.Should().BeNull();
        convertedBack.Should().BeNull();
    }

    [Fact]
    public void ConvertReusePolicyToDedupeStatuses_AllStatuses_ThenConvertBack_ReturnsPolicyWithAllStatuses()
    {
        // Arrange
        var policy = new P.OrchestrationIdReusePolicy();
        ImmutableArray<P.OrchestrationStatus> terminalStatuses = ProtoUtils.GetTerminalStatuses();
        foreach (var status in terminalStatuses)
        {
            policy.ReplaceableStatus.Add(status);
        }

        // Act
        P.OrchestrationStatus[]? dedupeStatuses = ProtoUtils.ConvertReusePolicyToDedupeStatuses(policy);
        P.OrchestrationIdReusePolicy? convertedBack = ProtoUtils.ConvertDedupeStatusesToReusePolicy(dedupeStatuses);

        // Assert
        // Policy with all statuses -> no dedupe statuses -> null
        // null dedupe statuses -> all are replaceable -> policy with all statuses
        dedupeStatuses.Should().BeNull();
        convertedBack.Should().NotBeNull();
        convertedBack!.ReplaceableStatus.Should().HaveCount(4);
        convertedBack.ReplaceableStatus.Should().BeEquivalentTo(policy.ReplaceableStatus);
    }

    [Fact]
    public void ConvertDedupeStatusesToReusePolicy_EmptyArray_ThenConvertBack_ReturnsNull()
    {
        // Arrange
        var dedupeStatuses = Array.Empty<P.OrchestrationStatus>();

        // Act
        P.OrchestrationIdReusePolicy? policy = ProtoUtils.ConvertDedupeStatusesToReusePolicy(dedupeStatuses);
        P.OrchestrationStatus[]? convertedBack = ProtoUtils.ConvertReusePolicyToDedupeStatuses(policy);

        // Assert
        // Empty dedupe statuses -> all terminal statuses are replaceable -> policy with all statuses
        // Policy with all statuses -> no dedupe statuses -> null
        policy.Should().NotBeNull();
        convertedBack.Should().BeNull();
    }

    [Fact]
    public void ConvertReusePolicyToDedupeStatuses_EmptyPolicy_ThenConvertBack_ReturnsPolicyWithAllStatuses()
    {
        // Arrange
        var policy = new P.OrchestrationIdReusePolicy();

        // Act
        P.OrchestrationStatus[]? dedupeStatuses = ProtoUtils.ConvertReusePolicyToDedupeStatuses(policy);
        P.OrchestrationIdReusePolicy? convertedBack = ProtoUtils.ConvertDedupeStatusesToReusePolicy(dedupeStatuses);

        // Assert
        // Empty policy (no replaceable statuses) -> ConvertReusePolicyToDedupeStatuses returns null
        // null dedupe statuses -> all terminal statuses are replaceable -> policy with all statuses
        dedupeStatuses.Should().BeNull();
        convertedBack.Should().NotBeNull();
        convertedBack!.ReplaceableStatus.Should().HaveCount(4);
    }

    [Theory]
    [InlineData(P.OrchestrationStatus.Completed)]
    [InlineData(P.OrchestrationStatus.Failed)]
    [InlineData(P.OrchestrationStatus.Terminated)]
    public void ConvertDedupeStatusesToReusePolicy_SingleStatus_ThenConvertBack_ReturnsOriginal(
        P.OrchestrationStatus dedupeStatus)
    {
        // Arrange
        var dedupeStatuses = new[] { dedupeStatus };

        // Act
        P.OrchestrationIdReusePolicy? policy = ProtoUtils.ConvertDedupeStatusesToReusePolicy(dedupeStatuses);
        P.OrchestrationStatus[]? convertedBack = ProtoUtils.ConvertReusePolicyToDedupeStatuses(policy);

        // Assert
        convertedBack.Should().NotBeNull();
        convertedBack!.Should().ContainSingle();
        convertedBack.Should().Contain(dedupeStatus);
    }

    [Fact]
    public void ConvertDedupeStatusesToReusePolicy_ThreeOutOfFourStatuses_ThenConvertBack_ReturnsOriginal()
    {
        // Arrange
        var dedupeStatuses = new[] 
        { 
            P.OrchestrationStatus.Completed, 
            P.OrchestrationStatus.Failed,
            P.OrchestrationStatus.Terminated
        };

        // Act
        P.OrchestrationIdReusePolicy? policy = ProtoUtils.ConvertDedupeStatusesToReusePolicy(dedupeStatuses);
        P.OrchestrationStatus[]? convertedBack = ProtoUtils.ConvertReusePolicyToDedupeStatuses(policy);

        // Assert
        convertedBack.Should().NotBeNull();
        convertedBack!.Should().HaveCount(3);
        convertedBack.Should().BeEquivalentTo(dedupeStatuses);
    }
}

