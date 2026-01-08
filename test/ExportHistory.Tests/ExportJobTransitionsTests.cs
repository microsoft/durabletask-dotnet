// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Xunit;

namespace Microsoft.DurableTask.ExportHistory.Tests;

public class ExportJobTransitionsTests
{
    [Theory]
    [InlineData(ExportJobStatus.Pending, ExportJobStatus.Active, true)]
    [InlineData(ExportJobStatus.Failed, ExportJobStatus.Active, true)]
    [InlineData(ExportJobStatus.Completed, ExportJobStatus.Active, true)]
    [InlineData(ExportJobStatus.Active, ExportJobStatus.Active, false)]
    [InlineData(ExportJobStatus.Active, ExportJobStatus.Failed, false)]
    [InlineData(ExportJobStatus.Active, ExportJobStatus.Completed, false)]
    public void IsValidTransition_Create_ValidatesCorrectly(
        ExportJobStatus from,
        ExportJobStatus to,
        bool expected)
    {
        // Act
        bool result = ExportJobTransitions.IsValidTransition("Create", from, to);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(ExportJobStatus.Active, ExportJobStatus.Completed, true)]
    [InlineData(ExportJobStatus.Pending, ExportJobStatus.Completed, false)]
    [InlineData(ExportJobStatus.Failed, ExportJobStatus.Completed, false)]
    [InlineData(ExportJobStatus.Completed, ExportJobStatus.Completed, false)]
    public void IsValidTransition_MarkAsCompleted_ValidatesCorrectly(
        ExportJobStatus from,
        ExportJobStatus to,
        bool expected)
    {
        // Act
        bool result = ExportJobTransitions.IsValidTransition("MarkAsCompleted", from, to);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(ExportJobStatus.Active, ExportJobStatus.Failed, true)]
    [InlineData(ExportJobStatus.Pending, ExportJobStatus.Failed, false)]
    [InlineData(ExportJobStatus.Failed, ExportJobStatus.Failed, false)]
    [InlineData(ExportJobStatus.Completed, ExportJobStatus.Failed, false)]
    public void IsValidTransition_MarkAsFailed_ValidatesCorrectly(
        ExportJobStatus from,
        ExportJobStatus to,
        bool expected)
    {
        // Act
        bool result = ExportJobTransitions.IsValidTransition("MarkAsFailed", from, to);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void IsValidTransition_UnknownOperation_ReturnsFalse()
    {
        // Arrange
        ExportJobStatus from = ExportJobStatus.Active;
        ExportJobStatus to = ExportJobStatus.Completed;

        // Act
        bool result = ExportJobTransitions.IsValidTransition("UnknownOperation", from, to);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("MarkAsCompleted")]
    [InlineData("MarkAsFailed")]
    public void IsValidTransition_AllValidTransitions_ReturnsTrue(string operationName)
    {
        // Arrange
        ExportJobStatus from = operationName switch
        {
            "Create" => ExportJobStatus.Pending,
            "MarkAsCompleted" => ExportJobStatus.Active,
            "MarkAsFailed" => ExportJobStatus.Active,
            _ => ExportJobStatus.Pending,
        };

        ExportJobStatus to = operationName switch
        {
            "Create" => ExportJobStatus.Active,
            "MarkAsCompleted" => ExportJobStatus.Completed,
            "MarkAsFailed" => ExportJobStatus.Failed,
            _ => ExportJobStatus.Active,
        };

        // Act
        bool result = ExportJobTransitions.IsValidTransition(operationName, from, to);

        // Assert
        result.Should().BeTrue();
    }
}

