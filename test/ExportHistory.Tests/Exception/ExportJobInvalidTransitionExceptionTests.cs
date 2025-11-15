// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Xunit;

namespace Microsoft.DurableTask.ExportHistory.Tests.Exception;

public class ExportJobInvalidTransitionExceptionTests
{
    [Fact]
    public void Constructor_WithAllParameters_CreatesInstance()
    {
        // Arrange
        string jobId = "job-123";
        ExportJobStatus fromStatus = ExportJobStatus.Active;
        ExportJobStatus toStatus = ExportJobStatus.Completed;
        string operationName = "MarkAsCompleted";

        // Act
        var exception = new ExportJobInvalidTransitionException(jobId, fromStatus, toStatus, operationName);

        // Assert
        exception.Should().NotBeNull();
        exception.JobId.Should().Be(jobId);
        exception.FromStatus.Should().Be(fromStatus);
        exception.ToStatus.Should().Be(toStatus);
        exception.OperationName.Should().Be(operationName);
        exception.Message.Should().Contain(jobId);
        exception.Message.Should().Contain(fromStatus.ToString());
        exception.Message.Should().Contain(toStatus.ToString());
        exception.Message.Should().Contain(operationName);
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithAllParametersAndInnerException_CreatesInstance()
    {
        // Arrange
        string jobId = "job-123";
        ExportJobStatus fromStatus = ExportJobStatus.Failed;
        ExportJobStatus toStatus = ExportJobStatus.Active;
        string operationName = "Create";
        System.Exception innerException = new InvalidOperationException("Inner error");

        // Act
        var exception = new ExportJobInvalidTransitionException(jobId, fromStatus, toStatus, operationName, innerException);

        // Assert
        exception.Should().NotBeNull();
        exception.JobId.Should().Be(jobId);
        exception.FromStatus.Should().Be(fromStatus);
        exception.ToStatus.Should().Be(toStatus);
        exception.OperationName.Should().Be(operationName);
        exception.InnerException.Should().Be(innerException);
    }

    [Theory]
    [InlineData(ExportJobStatus.Uninitialized, ExportJobStatus.Active)]
    [InlineData(ExportJobStatus.Active, ExportJobStatus.Completed)]
    [InlineData(ExportJobStatus.Active, ExportJobStatus.Failed)]
    [InlineData(ExportJobStatus.Failed, ExportJobStatus.Active)]
    [InlineData(ExportJobStatus.Completed, ExportJobStatus.Active)]
    public void Constructor_WithVariousStatusTransitions_CreatesInstance(
        ExportJobStatus fromStatus,
        ExportJobStatus toStatus)
    {
        // Arrange
        string jobId = "job-123";
        string operationName = "TestOperation";

        // Act
        var exception = new ExportJobInvalidTransitionException(jobId, fromStatus, toStatus, operationName);

        // Assert
        exception.FromStatus.Should().Be(fromStatus);
        exception.ToStatus.Should().Be(toStatus);
    }
}

