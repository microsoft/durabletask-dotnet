// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Xunit;

namespace Microsoft.DurableTask.ExportHistory.Tests.Models;

public class ExportModeTests
{
    [Fact]
    public void ExportMode_Values_AreCorrect()
    {
        // Assert
        ExportMode.Batch.Should().Be((ExportMode)1);
        ExportMode.Continuous.Should().Be((ExportMode)2);
    }

    [Theory]
    [InlineData(ExportMode.Batch, 1)]
    [InlineData(ExportMode.Continuous, 2)]
    public void ExportMode_EnumValue_MatchesExpected(ExportMode mode, int expectedValue)
    {
        // Assert
        ((int)mode).Should().Be(expectedValue);
    }
}

