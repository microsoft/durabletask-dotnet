// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Xunit;

namespace Microsoft.DurableTask.ExportHistory.Tests.Models;

public class CommitCheckpointRequestTests
{
    [Fact]
    public void Constructor_Default_CreatesInstance()
    {
        // Act
        var request = new CommitCheckpointRequest();

        // Assert
        request.Should().NotBeNull();
        request.ScannedInstances.Should().Be(0);
        request.ExportedInstances.Should().Be(0);
        request.Checkpoint.Should().BeNull();
        request.Failures.Should().BeNull();
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        // Arrange
        var request = new CommitCheckpointRequest();
        var checkpoint = new ExportCheckpoint("last-key");
        var failures = new List<ExportFailure>
        {
            new("instance-1", "error1", 1, DateTimeOffset.UtcNow),
            new("instance-2", "error2", 2, DateTimeOffset.UtcNow),
        };

        // Act
        request.ScannedInstances = 100;
        request.ExportedInstances = 95;
        request.Checkpoint = checkpoint;
        request.Failures = failures;

        // Assert
        request.ScannedInstances.Should().Be(100);
        request.ExportedInstances.Should().Be(95);
        request.Checkpoint.Should().Be(checkpoint);
        request.Failures.Should().BeEquivalentTo(failures);
    }

    [Fact]
    public void Properties_CanBeSetToNull()
    {
        // Arrange
        var request = new CommitCheckpointRequest
        {
            Checkpoint = new ExportCheckpoint("key"),
            Failures = new List<ExportFailure>(),
        };

        // Act
        request.Checkpoint = null;
        request.Failures = null;

        // Assert
        request.Checkpoint.Should().BeNull();
        request.Failures.Should().BeNull();
    }
}

