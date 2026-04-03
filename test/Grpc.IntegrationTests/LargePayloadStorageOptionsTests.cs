// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Grpc.Tests;

public class LargePayloadStorageOptionsTests
{
    [Fact]
    public void ThresholdBytes_ExactlyOneMiB_DoesNotThrow()
    {
        // Arrange
        LargePayloadStorageOptions options = new();

        // Act & Assert
        options.ThresholdBytes = 1 * 1024 * 1024;
        Assert.Equal(1 * 1024 * 1024, options.ThresholdBytes);
    }

    [Fact]
    public void ThresholdBytes_ExceedsOneMiB_ThrowsArgumentOutOfRange()
    {
        // Arrange
        LargePayloadStorageOptions options = new();

        // Act & Assert
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => options.ThresholdBytes = (1 * 1024 * 1024) + 1);
        Assert.Contains("Payload storage threshold", ex.Message);
    }

    [Fact]
    public void ThresholdBytes_DefaultValue_Is900000()
    {
        // Arrange & Act
        LargePayloadStorageOptions options = new();

        // Assert
        Assert.Equal(900_000, options.ThresholdBytes);
    }

    [Fact]
    public void MaxPayloadBytes_DefaultValue_Is10MB()
    {
        // Arrange & Act
        LargePayloadStorageOptions options = new();

        // Assert
        Assert.Equal(10 * 1024 * 1024, options.MaxPayloadBytes);
    }

    [Fact]
    public void CompressionEnabled_DefaultValue_IsTrue()
    {
        // Arrange & Act
        LargePayloadStorageOptions options = new();

        // Assert
        Assert.True(options.CompressionEnabled);
    }

    [Fact]
    public void ContainerName_DefaultValue_IsDurabletaskPayloads()
    {
        // Arrange & Act
        LargePayloadStorageOptions options = new();

        // Assert
        Assert.Equal("durabletask-payloads", options.ContainerName);
    }
}