// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.DurableTask.Converters;
using Xunit;

namespace Microsoft.DurableTask.ExportHistory.Tests.Models;

public class ExportCheckpointTests
{
    [Fact]
    public void Deserialize_LegacyCheckpoint_PreservesKeyWithoutWindow()
    {
        // Arrange
        string json = """{"LastInstanceKey":"legacy-key"}""";

        // Act
        ExportCheckpoint checkpoint = JsonDataConverter.Default.Deserialize<ExportCheckpoint>(json)!;

        // Assert
        Assert.Equal("legacy-key", checkpoint.LastInstanceKey);
        Assert.Null(checkpoint.CompletedTimeFrom);
        Assert.Null(checkpoint.CompletedTimeTo);
    }

    [Fact]
    public void Serialize_WindowCheckpoint_RoundTrips()
    {
        // Arrange
        ExportCheckpoint checkpoint = new("page-key")
        {
            CompletedTimeFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            CompletedTimeTo = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
        };

        // Act
        ExportCheckpoint? result = JsonDataConverter.Default.Deserialize<ExportCheckpoint>(
            JsonDataConverter.Default.Serialize(checkpoint));

        // Assert
        Assert.Equal(checkpoint, result);
    }

    [Fact]
    public void Constructor_WithNullLastInstanceKey_CreatesInstance()
    {
        // Act
        var checkpoint = new ExportCheckpoint(null);

        // Assert
        checkpoint.Should().NotBeNull();
        checkpoint.LastInstanceKey.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithLastInstanceKey_CreatesInstance()
    {
        // Arrange
        string lastInstanceKey = "instance-key-123";

        // Act
        var checkpoint = new ExportCheckpoint(lastInstanceKey);

        // Assert
        checkpoint.Should().NotBeNull();
        checkpoint.LastInstanceKey.Should().Be(lastInstanceKey);
    }

    [Fact]
    public void Constructor_WithDefaultParameter_CreatesInstance()
    {
        // Act
        var checkpoint = new ExportCheckpoint();

        // Assert
        checkpoint.Should().NotBeNull();
        checkpoint.LastInstanceKey.Should().BeNull();
    }

    [Fact]
    public void Record_Equality_Works()
    {
        // Arrange
        string key = "test-key";
        var checkpoint1 = new ExportCheckpoint(key);
        var checkpoint2 = new ExportCheckpoint(key);

        // Assert
        checkpoint1.Should().Be(checkpoint2);
        checkpoint1.GetHashCode().Should().Be(checkpoint2.GetHashCode());
    }

    [Fact]
    public void Record_Inequality_Works()
    {
        // Arrange
        var checkpoint1 = new ExportCheckpoint("key1");
        var checkpoint2 = new ExportCheckpoint("key2");

        // Assert
        checkpoint1.Should().NotBe(checkpoint2);
    }
}
