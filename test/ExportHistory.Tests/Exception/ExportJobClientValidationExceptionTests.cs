// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Xunit;

namespace Microsoft.DurableTask.ExportHistory.Tests.Exception;

public class ExportJobClientValidationExceptionTests
{
    [Fact]
    public void Constructor_WithJobIdAndMessage_CreatesInstance()
    {
        // Arrange
        string jobId = "job-123";
        string message = "Validation failed: invalid parameters";

        // Act
        var exception = new ExportJobClientValidationException(jobId, message);

        // Assert
        exception.Should().NotBeNull();
        exception.JobId.Should().Be(jobId);
        exception.Message.Should().Contain(jobId);
        exception.Message.Should().Contain(message);
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithJobIdMessageAndInnerException_CreatesInstance()
    {
        // Arrange
        string jobId = "job-123";
        string message = "Validation failed: invalid parameters";
        System.Exception innerException = new ArgumentException("Inner error");

        // Act
        var exception = new ExportJobClientValidationException(jobId, message, innerException);

        // Assert
        exception.Should().NotBeNull();
        exception.JobId.Should().Be(jobId);
        exception.Message.Should().Contain(jobId);
        exception.Message.Should().Contain(message);
        exception.InnerException.Should().Be(innerException);
    }

    [Theory]
    [InlineData("", "message")]
    [InlineData("job-123", "Validation failed")]
    public void Constructor_WithVariousParameters_CreatesInstance(string jobId, string message)
    {
        // Act
        var exception = new ExportJobClientValidationException(jobId, message);

        // Assert
        exception.JobId.Should().Be(jobId);
        if (!string.IsNullOrEmpty(message))
        {
            exception.Message.Should().Contain(message);
        }
    }

    [Fact]
    public void Constructor_WithEmptyMessage_CreatesInstance()
    {
        // Arrange
        string jobId = "job-123";
        string message = string.Empty;

        // Act
        var exception = new ExportJobClientValidationException(jobId, message);

        // Assert
        exception.JobId.Should().Be(jobId);
        exception.Message.Should().NotBeNullOrEmpty();
        exception.Message.Should().Contain(jobId);
    }
}

