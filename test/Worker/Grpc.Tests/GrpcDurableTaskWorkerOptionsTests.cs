// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

/// <summary>
/// Tests for <see cref="GrpcDurableTaskWorkerOptions"/> validation.
/// </summary>
public class GrpcDurableTaskWorkerOptionsTests
{
    [Fact]
    public void Default_CompleteOrchestrationWorkItemChunkSizeInBytes_IsWithinRange()
    {
        // Arrange
        var options = new GrpcDurableTaskWorkerOptions();

        // Act
        int value = options.CompleteOrchestrationWorkItemChunkSizeInBytes;

        // Assert
        value.Should().BeGreaterOrEqualTo(
            GrpcDurableTaskWorkerOptions.MinCompleteOrchestrationWorkItemChunkSizeInBytes);
        value.Should().BeLessOrEqualTo(
            GrpcDurableTaskWorkerOptions.MaxCompleteOrchestrationWorkItemChunkSizeBytes);
    }

    [Fact]
    public void Setting_CompleteOrchestrationWorkItemChunkSizeInBytes_BelowMin_Throws()
    {
        // Arrange
        var options = new GrpcDurableTaskWorkerOptions();
        int belowMin = GrpcDurableTaskWorkerOptions.MinCompleteOrchestrationWorkItemChunkSizeInBytes - 1;

        // Act
        Action act = () => options.CompleteOrchestrationWorkItemChunkSizeInBytes = belowMin;

        // Assert
        act.Should()
           .Throw<ArgumentOutOfRangeException>()
           .WithParameterName(nameof(GrpcDurableTaskWorkerOptions.CompleteOrchestrationWorkItemChunkSizeInBytes));
    }

    [Fact]
    public void Setting_CompleteOrchestrationWorkItemChunkSizeInBytes_AboveMax_Throws()
    {
        // Arrange
        var options = new GrpcDurableTaskWorkerOptions();
        int aboveMax = GrpcDurableTaskWorkerOptions.MaxCompleteOrchestrationWorkItemChunkSizeBytes + 1;

        // Act
        Action act = () => options.CompleteOrchestrationWorkItemChunkSizeInBytes = aboveMax;

        // Assert
        act.Should()
           .Throw<ArgumentOutOfRangeException>()
           .WithParameterName(nameof(GrpcDurableTaskWorkerOptions.CompleteOrchestrationWorkItemChunkSizeInBytes));
    }

    [Fact]
    public void Setting_CompleteOrchestrationWorkItemChunkSizeInBytes_AtMinBoundary_Succeeds()
    {
        // Arrange
        var options = new GrpcDurableTaskWorkerOptions();
        int minValue = GrpcDurableTaskWorkerOptions.MinCompleteOrchestrationWorkItemChunkSizeInBytes;

        // Act
        options.CompleteOrchestrationWorkItemChunkSizeInBytes = minValue;

        // Assert
        options.CompleteOrchestrationWorkItemChunkSizeInBytes.Should().Be(minValue);
    }

    [Fact]
    public void Setting_CompleteOrchestrationWorkItemChunkSizeInBytes_AtMaxBoundary_Succeeds()
    {
        // Arrange
        var options = new GrpcDurableTaskWorkerOptions();
        int maxValue = GrpcDurableTaskWorkerOptions.MaxCompleteOrchestrationWorkItemChunkSizeBytes;

        // Act
        options.CompleteOrchestrationWorkItemChunkSizeInBytes = maxValue;

        // Assert
        options.CompleteOrchestrationWorkItemChunkSizeInBytes.Should().Be(maxValue);
    }

    [Fact]
    public void Setting_CompleteOrchestrationWorkItemChunkSizeInBytes_WithinRange_Succeeds()
    {
        // Arrange
        var options = new GrpcDurableTaskWorkerOptions();
        int withinRange = 2 * 1024 * 1024; // 2 MB

        // Act
        options.CompleteOrchestrationWorkItemChunkSizeInBytes = withinRange;

        // Assert
        options.CompleteOrchestrationWorkItemChunkSizeInBytes.Should().Be(withinRange);
    }
}

