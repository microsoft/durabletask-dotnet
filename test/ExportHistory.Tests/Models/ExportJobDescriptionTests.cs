// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Xunit;

namespace Microsoft.DurableTask.ExportHistory.Tests.Models;

public class ExportJobDescriptionTests
{
    [Fact]
    public void Constructor_Default_CreatesInstance()
    {
        // Act
        var description = new ExportJobDescription();

        // Assert
        description.Should().NotBeNull();
        description.JobId.Should().BeEmpty();
        description.Status.Should().Be(ExportJobStatus.Pending);
        description.CreatedAt.Should().BeNull();
        description.LastModifiedAt.Should().BeNull();
        description.Config.Should().BeNull();
        description.OrchestratorInstanceId.Should().BeNull();
        description.ScannedInstances.Should().Be(0);
        description.ExportedInstances.Should().Be(0);
        description.LastError.Should().BeNull();
        description.Checkpoint.Should().BeNull();
        description.LastCheckpointTime.Should().BeNull();
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        // Arrange
        var description = new ExportJobDescription();
        var config = new ExportJobConfiguration(
            ExportMode.Batch,
            new ExportFilter(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow),
            new ExportDestination("container"),
            ExportFormat.Default);
        var checkpoint = new ExportCheckpoint("last-key");
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        description = description with
        {
            JobId = "job-123",
            Status = ExportJobStatus.Active,
            CreatedAt = now,
            LastModifiedAt = now,
            Config = config,
            OrchestratorInstanceId = "orchestrator-123",
            ScannedInstances = 100,
            ExportedInstances = 95,
            LastError = "test error",
            Checkpoint = checkpoint,
            LastCheckpointTime = now,
        };

        // Assert
        description.JobId.Should().Be("job-123");
        description.Status.Should().Be(ExportJobStatus.Active);
        description.CreatedAt.Should().Be(now);
        description.LastModifiedAt.Should().Be(now);
        description.Config.Should().Be(config);
        description.OrchestratorInstanceId.Should().Be("orchestrator-123");
        description.ScannedInstances.Should().Be(100);
        description.ExportedInstances.Should().Be(95);
        description.LastError.Should().Be("test error");
        description.Checkpoint.Should().Be(checkpoint);
        description.LastCheckpointTime.Should().Be(now);
    }
}

