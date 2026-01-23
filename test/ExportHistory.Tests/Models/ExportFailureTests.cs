// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Xunit;

namespace Microsoft.DurableTask.ExportHistory.Tests.Models;

public class ExportFailureTests
{
    [Fact]
    public void Constructor_WithAllParameters_CreatesInstance()
    {
        // Arrange
        string instanceId = "instance-123";
        string reason = "Export failed";
        int attemptCount = 3;
        DateTimeOffset lastAttempt = DateTimeOffset.UtcNow;

        // Act
        var failure = new ExportFailure(instanceId, reason, attemptCount, lastAttempt);

        // Assert
        failure.Should().NotBeNull();
        failure.InstanceId.Should().Be(instanceId);
        failure.Reason.Should().Be(reason);
        failure.AttemptCount.Should().Be(attemptCount);
        failure.LastAttempt.Should().Be(lastAttempt);
    }

    [Fact]
    public void Record_Equality_Works()
    {
        // Arrange
        string instanceId = "instance-123";
        string reason = "Export failed";
        int attemptCount = 3;
        DateTimeOffset lastAttempt = DateTimeOffset.UtcNow;

        var failure1 = new ExportFailure(instanceId, reason, attemptCount, lastAttempt);
        var failure2 = new ExportFailure(instanceId, reason, attemptCount, lastAttempt);

        // Assert
        failure1.Should().Be(failure2);
        failure1.GetHashCode().Should().Be(failure2.GetHashCode());
    }

    [Fact]
    public void Record_Inequality_Works()
    {
        // Arrange
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var failure1 = new ExportFailure("instance-1", "reason1", 1, now);
        var failure2 = new ExportFailure("instance-2", "reason2", 2, now);

        // Assert
        failure1.Should().NotBe(failure2);
    }
}

