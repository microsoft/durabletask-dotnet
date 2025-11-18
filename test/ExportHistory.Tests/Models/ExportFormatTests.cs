// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Xunit;

namespace Microsoft.DurableTask.ExportHistory.Tests.Models;

public class ExportFormatTests
{
    [Fact]
    public void Constructor_WithDefaultValues_CreatesInstance()
    {
        // Act
        var format = new ExportFormat();

        // Assert
        format.Should().NotBeNull();
        format.Kind.Should().Be(ExportFormatKind.Jsonl);
        format.SchemaVersion.Should().Be("1.0");
    }

    [Fact]
    public void Constructor_WithCustomValues_CreatesInstance()
    {
        // Arrange
        ExportFormatKind kind = ExportFormatKind.Json;
        string schemaVersion = "2.0";

        // Act
        var format = new ExportFormat(kind, schemaVersion);

        // Assert
        format.Should().NotBeNull();
        format.Kind.Should().Be(kind);
        format.SchemaVersion.Should().Be(schemaVersion);
    }

    [Fact]
    public void Default_ReturnsDefaultInstance()
    {
        // Act
        var format = ExportFormat.Default;

        // Assert
        format.Should().NotBeNull();
        format.Kind.Should().Be(ExportFormatKind.Jsonl);
        format.SchemaVersion.Should().Be("1.0");
    }

    [Fact]
    public void Default_IsImmutable()
    {
        // Arrange
        var format1 = ExportFormat.Default;
        var format2 = ExportFormat.Default;

        // Act & Assert
        format1.Should().Be(format2);
    }
}

