// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Xunit;

namespace Microsoft.DurableTask.ExportHistory.Tests.Models;

public class ExportJobQueryTests
{
    [Fact]
    public void DefaultPageSize_IsCorrect()
    {
        // Assert
        ExportJobQuery.DefaultPageSize.Should().Be(100);
    }

    [Fact]
    public void Constructor_Default_CreatesInstance()
    {
        // Act
        var query = new ExportJobQuery();

        // Assert
        query.Should().NotBeNull();
        query.Status.Should().BeNull();
        query.JobIdPrefix.Should().BeNull();
        query.CreatedFrom.Should().BeNull();
        query.CreatedTo.Should().BeNull();
        query.PageSize.Should().BeNull();
        query.ContinuationToken.Should().BeNull();
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        // Arrange
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        var query = new ExportJobQuery
        {
            Status = ExportJobStatus.Active,
            JobIdPrefix = "test-",
            CreatedFrom = now.AddDays(-1),
            CreatedTo = now,
            PageSize = 50,
            ContinuationToken = "token-123",
        };

        // Assert
        query.Status.Should().Be(ExportJobStatus.Active);
        query.JobIdPrefix.Should().Be("test-");
        query.CreatedFrom.Should().Be(now.AddDays(-1));
        query.CreatedTo.Should().Be(now);
        query.PageSize.Should().Be(50);
        query.ContinuationToken.Should().Be("token-123");
    }

    [Fact]
    public void Record_Equality_Works()
    {
        // Arrange
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var query1 = new ExportJobQuery
        {
            Status = ExportJobStatus.Active,
            JobIdPrefix = "test-",
            CreatedFrom = now.AddDays(-1),
            CreatedTo = now,
            PageSize = 50,
        };

        var query2 = new ExportJobQuery
        {
            Status = ExportJobStatus.Active,
            JobIdPrefix = "test-",
            CreatedFrom = now.AddDays(-1),
            CreatedTo = now,
            PageSize = 50,
        };

        // Assert
        query1.Should().Be(query2);
        query1.GetHashCode().Should().Be(query2.GetHashCode());
    }
}

