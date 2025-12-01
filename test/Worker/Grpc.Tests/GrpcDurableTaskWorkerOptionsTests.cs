// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

/// <summary>
/// Tests for <see cref="GrpcDurableTaskWorkerOptions"/> validation.
/// </summary>
public class GrpcDurableTaskWorkerOptionsTests
{
    [Fact]
    public void Default_MaxCompleteOrchestrationWorkItemSizePerChunk_IsWithinRange()
    {
        // Arrange
        var options = new GrpcDurableTaskWorkerOptions();

        // Act
        int value = options.MaxCompleteOrchestrationWorkItemSizePerChunk;

        // Assert
        value.Should().BeGreaterOrEqualTo(
            GrpcDurableTaskWorkerOptions.MinCompleteOrchestrationWorkItemSizePerChunkBytes);
        value.Should().BeLessOrEqualTo(
            GrpcDurableTaskWorkerOptions.MaxCompleteOrchestrationWorkItemSizePerChunkBytes);
    }

    [Fact]
    public void Setting_MaxCompleteOrchestrationWorkItemSizePerChunk_BelowMin_Throws()
    {
        // Arrange
        var options = new GrpcDurableTaskWorkerOptions();
        int belowMin = GrpcDurableTaskWorkerOptions.MinCompleteOrchestrationWorkItemSizePerChunkBytes - 1;

        // Act
        Action act = () => options.MaxCompleteOrchestrationWorkItemSizePerChunk = belowMin;

        // Assert
        act.Should()
           .Throw<ArgumentOutOfRangeException>()
           .WithParameterName(nameof(GrpcDurableTaskWorkerOptions.MaxCompleteOrchestrationWorkItemSizePerChunk));
    }

    [Fact]
    public void Setting_MaxCompleteOrchestrationWorkItemSizePerChunk_AboveMax_Throws()
    {
        // Arrange
        var options = new GrpcDurableTaskWorkerOptions();
        int aboveMax = GrpcDurableTaskWorkerOptions.MaxCompleteOrchestrationWorkItemSizePerChunkBytes + 1;

        // Act
        Action act = () => options.MaxCompleteOrchestrationWorkItemSizePerChunk = aboveMax;

        // Assert
        act.Should()
           .Throw<ArgumentOutOfRangeException>()
           .WithParameterName(nameof(GrpcDurableTaskWorkerOptions.MaxCompleteOrchestrationWorkItemSizePerChunk));
    }

    [Fact]
    public void Setting_MaxCompleteOrchestrationWorkItemSizePerChunk_AtMinBoundary_Succeeds()
    {
        // Arrange
        var options = new GrpcDurableTaskWorkerOptions();
        int minValue = GrpcDurableTaskWorkerOptions.MinCompleteOrchestrationWorkItemSizePerChunkBytes;

        // Act
        options.MaxCompleteOrchestrationWorkItemSizePerChunk = minValue;

        // Assert
        options.MaxCompleteOrchestrationWorkItemSizePerChunk.Should().Be(minValue);
    }

    [Fact]
    public void Setting_MaxCompleteOrchestrationWorkItemSizePerChunk_AtMaxBoundary_Succeeds()
    {
        // Arrange
        var options = new GrpcDurableTaskWorkerOptions();
        int maxValue = GrpcDurableTaskWorkerOptions.MaxCompleteOrchestrationWorkItemSizePerChunkBytes;

        // Act
        options.MaxCompleteOrchestrationWorkItemSizePerChunk = maxValue;

        // Assert
        options.MaxCompleteOrchestrationWorkItemSizePerChunk.Should().Be(maxValue);
    }

    [Fact]
    public void Setting_MaxCompleteOrchestrationWorkItemSizePerChunk_WithinRange_Succeeds()
    {
        // Arrange
        var options = new GrpcDurableTaskWorkerOptions();
        int withinRange = 2 * 1024 * 1024; // 2 MB

        // Act
        options.MaxCompleteOrchestrationWorkItemSizePerChunk = withinRange;

        // Assert
        options.MaxCompleteOrchestrationWorkItemSizePerChunk.Should().Be(withinRange);
    }
}

