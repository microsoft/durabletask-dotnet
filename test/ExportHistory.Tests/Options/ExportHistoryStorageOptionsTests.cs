// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Xunit;

namespace Microsoft.DurableTask.ExportHistory.Tests.Options;

public class ExportHistoryStorageOptionsTests
{
    [Fact]
    public void Constructor_Default_CreatesInstance()
    {
        // Act
        var options = new ExportHistoryStorageOptions();

        // Assert
        options.Should().NotBeNull();
        options.ConnectionString.Should().BeEmpty();
        options.ContainerName.Should().BeEmpty();
        options.Prefix.Should().BeNull();
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        // Arrange
        var options = new ExportHistoryStorageOptions();

        // Act
        options.ConnectionString = "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=key;EndpointSuffix=core.windows.net";
        options.ContainerName = "test-container";
        options.Prefix = "exports/";

        // Assert
        options.ConnectionString.Should().Be("DefaultEndpointsProtocol=https;AccountName=test;AccountKey=key;EndpointSuffix=core.windows.net");
        options.ContainerName.Should().Be("test-container");
        options.Prefix.Should().Be("exports/");
    }

    [Fact]
    public void Prefix_CanBeSetToNull()
    {
        // Arrange
        var options = new ExportHistoryStorageOptions
        {
            Prefix = "test-prefix",
        };

        // Act
        options.Prefix = null;

        // Assert
        options.Prefix.Should().BeNull();
    }
}

