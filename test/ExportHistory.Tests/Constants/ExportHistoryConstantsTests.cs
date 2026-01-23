// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Xunit;

namespace Microsoft.DurableTask.ExportHistory.Tests.Constants;

public class ExportHistoryConstantsTests
{
    [Fact]
    public void OrchestratorInstanceIdPrefix_IsCorrect()
    {
        // Assert
        ExportHistoryConstants.OrchestratorInstanceIdPrefix.Should().Be("ExportJob-");
    }

    [Fact]
    public void GetOrchestratorInstanceId_WithJobId_ReturnsCorrectFormat()
    {
        // Arrange
        string jobId = "test-job-123";

        // Act
        string instanceId = ExportHistoryConstants.GetOrchestratorInstanceId(jobId);

        // Assert
        instanceId.Should().Be("ExportJob-test-job-123");
        instanceId.Should().StartWith(ExportHistoryConstants.OrchestratorInstanceIdPrefix);
    }

    [Theory]
    [InlineData("job-1")]
    [InlineData("very-long-job-id-with-special-characters")]
    [InlineData("")]
    public void GetOrchestratorInstanceId_WithVariousJobIds_ReturnsCorrectFormat(string jobId)
    {
        // Act
        string instanceId = ExportHistoryConstants.GetOrchestratorInstanceId(jobId);

        // Assert
        instanceId.Should().Be($"{ExportHistoryConstants.OrchestratorInstanceIdPrefix}{jobId}");
    }
}

