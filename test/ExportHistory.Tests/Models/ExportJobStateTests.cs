// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Xunit;

namespace Microsoft.DurableTask.ExportHistory.Tests.Models;

public class ExportJobStateTests
{
    [Fact]
    public void Constructor_Default_CreatesInstance()
    {
        // Act
        var state = new ExportJobState();

        // Assert
        state.Should().NotBeNull();
        state.Status.Should().Be(ExportJobStatus.Pending);
        state.Config.Should().BeNull();
        state.Checkpoint.Should().BeNull();
        state.CreatedAt.Should().BeNull();
        state.LastModifiedAt.Should().BeNull();
        state.LastCheckpointTime.Should().BeNull();
        state.LastError.Should().BeNull();
        state.ScannedInstances.Should().Be(0);
        state.ExportedInstances.Should().Be(0);
        state.OrchestratorInstanceId.Should().BeNull();
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        // Arrange
        var state = new ExportJobState();
        var config = new ExportJobConfiguration(
            ExportMode.Batch,
            new ExportFilter(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow),
            new ExportDestination("container"),
            ExportFormat.Default);
        var checkpoint = new ExportCheckpoint("last-key");
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        state.Status = ExportJobStatus.Active;
        state.Config = config;
        state.Checkpoint = checkpoint;
        state.CreatedAt = now;
        state.LastModifiedAt = now;
        state.LastCheckpointTime = now;
        state.LastError = "test error";
        state.ScannedInstances = 100;
        state.ExportedInstances = 95;
        state.OrchestratorInstanceId = "orchestrator-123";

        // Assert
        state.Status.Should().Be(ExportJobStatus.Active);
        state.Config.Should().Be(config);
        state.Checkpoint.Should().Be(checkpoint);
        state.CreatedAt.Should().Be(now);
        state.LastModifiedAt.Should().Be(now);
        state.LastCheckpointTime.Should().Be(now);
        state.LastError.Should().Be("test error");
        state.ScannedInstances.Should().Be(100);
        state.ExportedInstances.Should().Be(95);
        state.OrchestratorInstanceId.Should().Be("orchestrator-123");
    }
}

