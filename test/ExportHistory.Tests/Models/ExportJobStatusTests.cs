// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Xunit;

namespace Microsoft.DurableTask.ExportHistory.Tests.Models;

public class ExportJobStatusTests
{
    [Fact]
    public void ExportJobStatus_Values_AreDefined()
    {
        // Assert
        Enum.GetValues<ExportJobStatus>().Should().HaveCount(4);
        Enum.GetValues<ExportJobStatus>().Should().Contain(ExportJobStatus.Uninitialized);
        Enum.GetValues<ExportJobStatus>().Should().Contain(ExportJobStatus.Active);
        Enum.GetValues<ExportJobStatus>().Should().Contain(ExportJobStatus.Failed);
        Enum.GetValues<ExportJobStatus>().Should().Contain(ExportJobStatus.Completed);
    }

    [Theory]
    [InlineData(ExportJobStatus.Uninitialized)]
    [InlineData(ExportJobStatus.Active)]
    [InlineData(ExportJobStatus.Failed)]
    [InlineData(ExportJobStatus.Completed)]
    public void ExportJobStatus_CanBeAssigned(ExportJobStatus status)
    {
        // Arrange
        ExportJobStatus assignedStatus = status;

        // Assert
        assignedStatus.Should().Be(status);
    }
}

