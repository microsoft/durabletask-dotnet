// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Xunit;

namespace Microsoft.DurableTask.ExportHistory.Tests.Models;

public class ExportJobConfigurationTests
{
    [Fact]
    public void Constructor_WithRequiredParameters_CreatesInstance()
    {
        // Arrange
        ExportMode mode = ExportMode.Batch;
        ExportFilter filter = new(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow);
        ExportDestination destination = new("test-container");
        ExportFormat format = ExportFormat.Default;

        // Act
        var config = new ExportJobConfiguration(mode, filter, destination, format);

        // Assert
        config.Should().NotBeNull();
        config.Mode.Should().Be(mode);
        config.Filter.Should().Be(filter);
        config.Destination.Should().Be(destination);
        config.Format.Should().Be(format);
        config.MaxParallelExports.Should().Be(32);
        config.MaxInstancesPerBatch.Should().Be(100);
    }

    [Fact]
    public void Constructor_WithAllParameters_CreatesInstance()
    {
        // Arrange
        ExportMode mode = ExportMode.Continuous;
        ExportFilter filter = new(DateTimeOffset.UtcNow.AddDays(-1));
        ExportDestination destination = new("test-container");
        ExportFormat format = ExportFormat.Default;
        int maxParallelExports = 64;
        int maxInstancesPerBatch = 200;

        // Act
        var config = new ExportJobConfiguration(mode, filter, destination, format, maxParallelExports, maxInstancesPerBatch);

        // Assert
        config.Should().NotBeNull();
        config.Mode.Should().Be(mode);
        config.Filter.Should().Be(filter);
        config.Destination.Should().Be(destination);
        config.Format.Should().Be(format);
        config.MaxParallelExports.Should().Be(maxParallelExports);
        config.MaxInstancesPerBatch.Should().Be(maxInstancesPerBatch);
    }

    [Fact]
    public void Record_Equality_Works()
    {
        // Arrange
        ExportMode mode = ExportMode.Batch;
        ExportFilter filter = new(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow);
        ExportDestination destination = new("test-container");
        ExportFormat format = ExportFormat.Default;

        var config1 = new ExportJobConfiguration(mode, filter, destination, format);
        var config2 = new ExportJobConfiguration(mode, filter, destination, format);

        // Assert
        config1.Should().Be(config2);
        config1.GetHashCode().Should().Be(config2.GetHashCode());
    }
}

