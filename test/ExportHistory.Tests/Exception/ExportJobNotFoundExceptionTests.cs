// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Xunit;

namespace Microsoft.DurableTask.ExportHistory.Tests.Exception;

public class ExportJobNotFoundExceptionTests
{
    [Fact]
    public void Constructor_WithJobId_CreatesInstance()
    {
        // Arrange
        string jobId = "job-123";

        // Act
        var exception = new ExportJobNotFoundException(jobId);

        // Assert
        exception.Should().NotBeNull();
        exception.JobId.Should().Be(jobId);
        exception.Message.Should().Contain(jobId);
        exception.Message.Should().Contain("was not found");
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithJobIdAndInnerException_CreatesInstance()
    {
        // Arrange
        string jobId = "job-123";
        System.Exception innerException = new InvalidOperationException("Inner error");

        // Act
        var exception = new ExportJobNotFoundException(jobId, innerException);

        // Assert
        exception.Should().NotBeNull();
        exception.JobId.Should().Be(jobId);
        exception.Message.Should().Contain(jobId);
        exception.Message.Should().Contain("was not found");
        exception.InnerException.Should().Be(innerException);
    }

    [Theory]
    [InlineData("")]
    [InlineData("job-123")]
    [InlineData("very-long-job-id-with-special-characters-12345")]
    public void Constructor_WithVariousJobIds_CreatesInstance(string jobId)
    {
        // Act
        var exception = new ExportJobNotFoundException(jobId);

        // Assert
        exception.JobId.Should().Be(jobId);
    }
}

