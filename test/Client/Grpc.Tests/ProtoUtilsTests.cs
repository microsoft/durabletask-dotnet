// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Client.Grpc.Tests;

public class ProtoUtilsTests
{
    [Fact]
    public void GetAllStatuses_ReturnsExpectedStatuses()
    {
        // Act
        ImmutableArray<P.OrchestrationStatus> allStatuses = ProtoUtils.GetAllStatuses();

        // Assert
        allStatuses.Should().HaveCount(7);
        allStatuses.Should().Contain(P.OrchestrationStatus.Completed);
        allStatuses.Should().Contain(P.OrchestrationStatus.Failed);
        allStatuses.Should().Contain(P.OrchestrationStatus.Terminated);
#pragma warning disable CS0618 // Type or member is obsolete
        allStatuses.Should().Contain(P.OrchestrationStatus.Canceled);
        allStatuses.Should().Contain(P.OrchestrationStatus.Pending);
        allStatuses.Should().Contain(P.OrchestrationStatus.Running);
        allStatuses.Should().Contain(P.OrchestrationStatus.Suspended);

#pragma warning restore CS0618
    }

    [Fact]
    public void GetAllStatuses_ReturnsImmutableArray()
    {
        // Act
        ImmutableArray<P.OrchestrationStatus> allStatuses = ProtoUtils.GetAllStatuses();

        // Assert
        allStatuses.IsDefault.Should().BeFalse();
        allStatuses.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void ConvertDedupeStatusesToReusePolicy_EmptyArray_ReturnsPolicyWithAllStatuses()
    {
        // Arrange
        var dedupeStatuses = Array.Empty<P.OrchestrationStatus>();

        // Act
        P.OrchestrationIdReusePolicy result = ProtoUtils.ConvertDedupeStatusesToReusePolicy(dedupeStatuses);

        // Assert
        // Empty array means no dedupe statuses, so all statuses are replaceable
        result.Should().NotBeNull();
        result!.ReplaceableStatus.Should().HaveCount(7);
    }

    [Fact]
    public void ConvertDedupeStatusesToReusePolicy_NullArray_ThrowsArgumentNullException()
    {
        // Arrange and Act
        Action act = () => ProtoUtils.ConvertDedupeStatusesToReusePolicy(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ConvertDedupeStatusesToReusePolicy_AllStatuses_ReturnsPolicyWithNoReplaceableStatuses()
    {
        // Arrange
        ImmutableArray<P.OrchestrationStatus> allStatuses = ProtoUtils.GetAllStatuses();
        var dedupeStatuses = allStatuses.ToArray();

        // Act
        P.OrchestrationIdReusePolicy result = ProtoUtils.ConvertDedupeStatusesToReusePolicy(dedupeStatuses);

        // Assert
        result.ReplaceableStatus.Should().BeEmpty();
    }

    [Fact]
    public void ConvertDedupeStatusesToReusePolicy_NoDedupeStatuses_ReturnsPolicyWithAllStatuses()
    {
        // Arrange
        var dedupeStatuses = Array.Empty<P.OrchestrationStatus>();

        // Act
        P.OrchestrationIdReusePolicy result = ProtoUtils.ConvertDedupeStatusesToReusePolicy(dedupeStatuses);

        // Assert
        // When no dedupe statuses, all statuses should be replaceable
        result.Should().NotBeNull();
        result!.ReplaceableStatus.Should().HaveCount(7);
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Completed);
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Failed);
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Terminated);
#pragma warning disable CS0618 // Type or member is obsolete
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Canceled);
#pragma warning restore CS0618
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Pending);
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Running);
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Suspended);
    }

    [Fact]
    public void ConvertDedupeStatusesToReusePolicy_SingleDedupeStatus_ReturnsPolicyWithRemainingStatuses()
    {
        // Arrange
        var dedupeStatuses = new[] { P.OrchestrationStatus.Running };

        // Act
        P.OrchestrationIdReusePolicy result = ProtoUtils.ConvertDedupeStatusesToReusePolicy(dedupeStatuses);

        // Assert
        result.Should().NotBeNull();
        result!.ReplaceableStatus.Should().HaveCount(6);
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Failed);
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Terminated);
#pragma warning disable CS0618 // Type or member is obsolete
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Canceled);
#pragma warning restore CS0618
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Completed);
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Pending);
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Suspended);
        result.ReplaceableStatus.Should().NotContain(P.OrchestrationStatus.Running);

    }

    [Fact]
    public void ConvertDedupeStatusesToReusePolicy_MultipleDedupeStatuses_ReturnsPolicyWithRemainingStatuses()
    {
        // Arrange
        var dedupeStatuses = new[] 
        { 
            P.OrchestrationStatus.Completed, 
            P.OrchestrationStatus.Failed,
            P.OrchestrationStatus.Pending
        };

        // Act
        P.OrchestrationIdReusePolicy result = ProtoUtils.ConvertDedupeStatusesToReusePolicy(dedupeStatuses);

        // Assert
        result.Should().NotBeNull();
        result!.ReplaceableStatus.Should().HaveCount(4);
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Terminated);
#pragma warning disable CS0618 // Type or member is obsolete
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Canceled);
#pragma warning restore CS0618
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Running);
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Suspended);
        result.ReplaceableStatus.Should().NotContain(P.OrchestrationStatus.Completed);
        result.ReplaceableStatus.Should().NotContain(P.OrchestrationStatus.Failed);
        result.ReplaceableStatus.Should().NotContain(P.OrchestrationStatus.Pending);
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
        P.OrchestrationIdReusePolicy result = ProtoUtils.ConvertDedupeStatusesToReusePolicy(dedupeStatuses);

        // Assert
        result.Should().NotBeNull();
        result!.ReplaceableStatus.Should().HaveCount(5);
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Terminated);
#pragma warning disable CS0618 // Type or member is obsolete
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Canceled);
#pragma warning restore CS0618
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Pending);
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Running);
        result.ReplaceableStatus.Should().Contain(P.OrchestrationStatus.Suspended);
    }

    [Fact]
    public void ConvertReusePolicyToDedupeStatuses_EmptyPolicy_ReturnsAllStatuses()
    {
        // Arrange
        var policy = new P.OrchestrationIdReusePolicy();

        // Act
        P.OrchestrationStatus[]? result = ProtoUtils.ConvertReusePolicyToDedupeStatuses(policy);

        // Assert
        result.Should().Equal(ProtoUtils.GetAllStatuses());
    }

    [Fact]
    public void ConvertReusePolicyToDedupeStatuses_AllStatusesReplaceable_ReturnsEmpty()
    {
        // Arrange
        var policy = new P.OrchestrationIdReusePolicy();
        ImmutableArray<P.OrchestrationStatus> allStatuses = ProtoUtils.GetAllStatuses();
        foreach (var status in allStatuses)
        {
            policy.ReplaceableStatus.Add(status);
        }

        // Act
        P.OrchestrationStatus[] result = ProtoUtils.ConvertReusePolicyToDedupeStatuses(policy);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
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
    public void ConvertReusePolicyToDedupeStatuses_SingleReplaceableStatus_ReturnsRemainingStatuses()
    {
        // Arrange
        var policy = new P.OrchestrationIdReusePolicy();
        policy.ReplaceableStatus.Add(P.OrchestrationStatus.Completed);

        // Act
        P.OrchestrationStatus[]? result = ProtoUtils.ConvertReusePolicyToDedupeStatuses(policy);

        // Assert
        result.Should().NotBeNull();
        result!.Should().HaveCount(6);
        result.Should().Contain(P.OrchestrationStatus.Failed);
        result.Should().Contain(P.OrchestrationStatus.Terminated);
#pragma warning disable CS0618 // Type or member is obsolete
        result.Should().Contain(P.OrchestrationStatus.Canceled);
#pragma warning restore CS0618
        result.Should().Contain(P.OrchestrationStatus.Running);
        result.Should().Contain(P.OrchestrationStatus.Pending);
        result.Should().Contain(P.OrchestrationStatus.Suspended);
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
        result!.Should().HaveCount(5);
        result.Should().Contain(P.OrchestrationStatus.Terminated);
#pragma warning disable CS0618 // Type or member is obsolete
        result.Should().Contain(P.OrchestrationStatus.Canceled);
#pragma warning restore CS0618
        result.Should().Contain(P.OrchestrationStatus.Running);
        result.Should().Contain(P.OrchestrationStatus.Pending);
        result.Should().Contain(P.OrchestrationStatus.Suspended);
        result.Should().NotContain(P.OrchestrationStatus.Completed);
        result.Should().NotContain(P.OrchestrationStatus.Failed);
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
        result!.Should().HaveCount(5);
        result.Should().Contain(P.OrchestrationStatus.Terminated);
#pragma warning disable CS0618 // Type or member is obsolete
        result.Should().Contain(P.OrchestrationStatus.Canceled);
#pragma warning restore CS0618
        result.Should().Contain(P.OrchestrationStatus.Running);
        result.Should().Contain(P.OrchestrationStatus.Pending);
        result.Should().Contain(P.OrchestrationStatus.Suspended);
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
        P.OrchestrationIdReusePolicy policy = ProtoUtils.ConvertDedupeStatusesToReusePolicy(originalDedupeStatuses);
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
    public void ConvertDedupeStatusesToReusePolicy_AllStatuses_ThenConvertBack_IsOriginal()
    {
        // Arrange
        ImmutableArray<P.OrchestrationStatus> allStatuses = ProtoUtils.GetAllStatuses();
        var dedupeStatuses = allStatuses.ToArray();

        // Act
        P.OrchestrationIdReusePolicy policy = ProtoUtils.ConvertDedupeStatusesToReusePolicy(dedupeStatuses);
        P.OrchestrationStatus[]? convertedBack = ProtoUtils.ConvertReusePolicyToDedupeStatuses(policy);

        // Assert
        policy.ReplaceableStatus.Should().BeEmpty();
        convertedBack.Should().NotBeNull();
        convertedBack.Should().Equal(dedupeStatuses);
    }

    [Fact]
    public void ConvertReusePolicyToDedupeStatuses_AllStatuses_ThenConvertBack_ReturnsPolicyWithAllStatuses()
    {
        // Arrange
        var policy = new P.OrchestrationIdReusePolicy();
        ImmutableArray<P.OrchestrationStatus> allStatuses = ProtoUtils.GetAllStatuses();
        foreach (var status in allStatuses)
        {
            policy.ReplaceableStatus.Add(status);
        }

        // Act
        P.OrchestrationStatus[]? dedupeStatuses = ProtoUtils.ConvertReusePolicyToDedupeStatuses(policy);
        dedupeStatuses.Should().NotBeNull();
        P.OrchestrationIdReusePolicy convertedBack = ProtoUtils.ConvertDedupeStatusesToReusePolicy(dedupeStatuses!);

        // Assert
        // Policy with all statuses -> no dedupe statuses
        // no dedupe statuses -> all are replaceable -> policy with all statuses
        dedupeStatuses.Should().BeEmpty();
        convertedBack.Should().NotBeNull();
        convertedBack!.ReplaceableStatus.Should().HaveCount(7);
        convertedBack.ReplaceableStatus.Should().BeEquivalentTo(policy.ReplaceableStatus);
    }

    [Fact]
    public void ConvertDedupeStatusesToReusePolicy_EmptyArray_ThenConvertBack_ReturnsEmpty()
    {
        // Arrange
        var dedupeStatuses = Array.Empty<P.OrchestrationStatus>();

        // Act
        P.OrchestrationIdReusePolicy policy = ProtoUtils.ConvertDedupeStatusesToReusePolicy(dedupeStatuses);
        P.OrchestrationStatus[]? convertedBack = ProtoUtils.ConvertReusePolicyToDedupeStatuses(policy);

        // Assert
        // Empty dedupe statuses -> all statuses are replaceable -> policy with all statuses
        // Policy with all statuses -> no dedupe statuses
        policy.Should().NotBeNull();
        policy.ReplaceableStatus.Should().HaveCount(7);
        convertedBack.Should().NotBeNull();
        convertedBack.Should().BeEmpty();
    }

    [Fact]
    public void ConvertReusePolicyToDedupeStatuses_EmptyPolicy_ThenConvertBack_ReturnsPolicyWithAllStatuses()
    {
        // Arrange
        var policy = new P.OrchestrationIdReusePolicy();

        // Act
        P.OrchestrationStatus[]? dedupeStatuses = ProtoUtils.ConvertReusePolicyToDedupeStatuses(policy);
        P.OrchestrationIdReusePolicy convertedBack = ProtoUtils.ConvertDedupeStatusesToReusePolicy(dedupeStatuses);

        // Assert
        // Empty policy (no replaceable statuses) -> ConvertReusePolicyToDedupeStatuses returns all statuses
        // all statuses deduped -> no statuses are replaceable -> policy with no statuses
        dedupeStatuses.Should().Equal(ProtoUtils.GetAllStatuses());
        convertedBack.ReplaceableStatus.Should().BeEmpty();
    }

    [Theory]
    [InlineData(P.OrchestrationStatus.Completed)]
    [InlineData(P.OrchestrationStatus.Failed)]
    [InlineData(P.OrchestrationStatus.Terminated)]
    [InlineData(P.OrchestrationStatus.Pending)]
    [InlineData(P.OrchestrationStatus.Running)]
    [InlineData(P.OrchestrationStatus.Suspended)]
    public void ConvertDedupeStatusesToReusePolicy_SingleStatus_ThenConvertBack_ReturnsOriginal(
        P.OrchestrationStatus dedupeStatus)
    {
        // Arrange
        var dedupeStatuses = new[] { dedupeStatus };

        // Act
        P.OrchestrationIdReusePolicy policy = ProtoUtils.ConvertDedupeStatusesToReusePolicy(dedupeStatuses);
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
        P.OrchestrationIdReusePolicy policy = ProtoUtils.ConvertDedupeStatusesToReusePolicy(dedupeStatuses);
        P.OrchestrationStatus[]? convertedBack = ProtoUtils.ConvertReusePolicyToDedupeStatuses(policy);

        // Assert
        convertedBack.Should().NotBeNull();
        convertedBack!.Should().HaveCount(3);
        convertedBack.Should().BeEquivalentTo(dedupeStatuses);
    }
}

