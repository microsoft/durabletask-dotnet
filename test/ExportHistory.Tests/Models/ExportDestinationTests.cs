// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Xunit;

namespace Microsoft.DurableTask.ExportHistory.Tests.Models;

public class ExportDestinationTests
{
    [Fact]
    public void Constructor_Default_CreatesInstance()
    {
        // Act
        var destination = new ExportDestination();

        // Assert
        destination.Should().NotBeNull();
        destination.Container.Should().BeNull();
        destination.Prefix.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithContainer_CreatesInstance()
    {
        // Arrange
        string container = "test-container";

        // Act
        var destination = new ExportDestination(container);

        // Assert
        destination.Should().NotBeNull();
        destination.Container.Should().Be(container);
        destination.Prefix.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Constructor_WithNullOrEmptyContainer_ThrowsArgumentException(string? container)
    {
        // Act
        Action act = () => new ExportDestination(container!);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithWhitespaceContainer_DoesNotThrow()
    {
        // Arrange
        // Check.NotNullOrEmpty only checks for null, empty, or strings starting with '\0'
        // It does NOT check for whitespace-only strings, so "   " is valid
        string container = "   ";

        // Act
        var destination = new ExportDestination(container);

        // Assert
        destination.Should().NotBeNull();
        destination.Container.Should().Be(container);
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        // Arrange
        var destination = new ExportDestination("test-container");

        // Act
        destination.Prefix = "test-prefix/";

        // Assert
        destination.Container.Should().Be("test-container");
        destination.Prefix.Should().Be("test-prefix/");
    }
}

