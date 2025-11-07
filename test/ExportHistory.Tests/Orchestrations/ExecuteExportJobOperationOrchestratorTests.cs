// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Moq;
using Xunit;

namespace Microsoft.DurableTask.ExportHistory.Tests.Orchestrations;

public class ExecuteExportJobOperationOrchestratorTests
{
    readonly Mock<TaskOrchestrationContext> mockContext;
    readonly Mock<TaskOrchestrationEntityFeature> mockEntityClient;
    readonly ExecuteExportJobOperationOrchestrator orchestrator;

    public ExecuteExportJobOperationOrchestratorTests()
    {
        this.mockContext = new Mock<TaskOrchestrationContext>(MockBehavior.Strict);
        this.mockEntityClient = new Mock<TaskOrchestrationEntityFeature>(MockBehavior.Loose);
        this.mockContext.Setup(c => c.Entities).Returns(this.mockEntityClient.Object);
        this.orchestrator = new ExecuteExportJobOperationOrchestrator();
    }

    [Fact]
    public async Task RunAsync_ValidRequest_CallsEntityOperation()
    {
        // Arrange
        var entityId = new EntityInstanceId(nameof(ExportJob), "test-job");
        string operationName = "Create";
        var input = new ExportJobCreationOptions(
            ExportMode.Batch,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            new ExportDestination("container"));
        var expectedResult = new ExportJobState { Status = ExportJobStatus.Active };
        var request = new ExportJobOperationRequest(entityId, operationName, input);

        this.mockEntityClient
            .Setup(e => e.CallEntityAsync<object>(entityId, operationName, input, default))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await this.orchestrator.RunAsync(this.mockContext.Object, request);

        // Assert
        result.Should().Be(expectedResult);
        this.mockEntityClient.Verify(
            e => e.CallEntityAsync<object>(entityId, operationName, input, default),
            Times.Once);
    }

    [Fact]
    public async Task RunAsync_WithNullInput_CallsEntityOperation()
    {
        // Arrange
        var entityId = new EntityInstanceId(nameof(ExportJob), "test-job");
        string operationName = "Get";
        var request = new ExportJobOperationRequest(entityId, operationName, null);

        this.mockEntityClient
            .Setup(e => e.CallEntityAsync<object>(entityId, operationName, null, default))
            .ReturnsAsync(new ExportJobState());

        // Act
        var result = await this.orchestrator.RunAsync(this.mockContext.Object, request);

        // Assert
        result.Should().NotBeNull();
        this.mockEntityClient.Verify(
            e => e.CallEntityAsync<object>(entityId, operationName, null, default),
            Times.Once);
    }

    [Fact]
    public async Task RunAsync_WithDeleteOperation_CallsEntityOperation()
    {
        // Arrange
        var entityId = new EntityInstanceId(nameof(ExportJob), "test-job");
        string operationName = ExportJobOperations.Delete;
        var request = new ExportJobOperationRequest(entityId, operationName, null);

        this.mockEntityClient
            .Setup(e => e.CallEntityAsync<object>(entityId, operationName, null, default))
            .ReturnsAsync(null!);

        // Act
        var result = await this.orchestrator.RunAsync(this.mockContext.Object, request);

        // Assert
        result.Should().BeNull();
        this.mockEntityClient.Verify(
            e => e.CallEntityAsync<object>(entityId, operationName, null, default),
            Times.Once);
    }
}

