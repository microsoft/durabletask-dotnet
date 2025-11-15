// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.DurableTask.Client;
using Xunit;

namespace Microsoft.DurableTask.ExportHistory.Tests.Models;

public class ExportFilterTests
{
    [Fact]
    public void Constructor_WithRequiredParameters_CreatesInstance()
    {
        // Arrange
        DateTimeOffset completedTimeFrom = DateTimeOffset.UtcNow.AddDays(-1);

        // Act
        var filter = new ExportFilter(completedTimeFrom);

        // Assert
        filter.Should().NotBeNull();
        filter.CompletedTimeFrom.Should().Be(completedTimeFrom);
        filter.CompletedTimeTo.Should().BeNull();
        filter.RuntimeStatus.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithAllParameters_CreatesInstance()
    {
        // Arrange
        DateTimeOffset completedTimeFrom = DateTimeOffset.UtcNow.AddDays(-1);
        DateTimeOffset completedTimeTo = DateTimeOffset.UtcNow;
        List<OrchestrationRuntimeStatus> runtimeStatus = new()
        {
            OrchestrationRuntimeStatus.Completed,
            OrchestrationRuntimeStatus.Failed,
        };

        // Act
        var filter = new ExportFilter(completedTimeFrom, completedTimeTo, runtimeStatus);

        // Assert
        filter.Should().NotBeNull();
        filter.CompletedTimeFrom.Should().Be(completedTimeFrom);
        filter.CompletedTimeTo.Should().Be(completedTimeTo);
        filter.RuntimeStatus.Should().BeEquivalentTo(runtimeStatus);
    }

    [Fact]
    public void Record_Equality_Works()
    {
        // Arrange
        DateTimeOffset from = DateTimeOffset.UtcNow.AddDays(-1);
        DateTimeOffset to = DateTimeOffset.UtcNow;
        var statuses = new List<OrchestrationRuntimeStatus> { OrchestrationRuntimeStatus.Completed };

        var filter1 = new ExportFilter(from, to, statuses);
        var filter2 = new ExportFilter(from, to, statuses);

        // Assert
        filter1.Should().Be(filter2);
        filter1.GetHashCode().Should().Be(filter2.GetHashCode());
    }
}

