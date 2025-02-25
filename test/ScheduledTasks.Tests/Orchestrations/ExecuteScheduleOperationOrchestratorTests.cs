// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Moq;
using Xunit;

namespace Microsoft.DurableTask.ScheduledTasks.Tests.Orchestrations;

public class ExecuteScheduleOperationOrchestratorTests
{
    private readonly Mock<TaskOrchestrationContext> mockContext;
    private readonly Mock<TaskOrchestrationEntityFeature> mockEntityClient;
    private readonly ExecuteScheduleOperationOrchestrator orchestrator;

    public ExecuteScheduleOperationOrchestratorTests()
    {
        this.mockContext = new Mock<TaskOrchestrationContext>();
        this.mockEntityClient = new Mock<TaskOrchestrationEntityFeature>();
        this.mockContext.Setup(c => c.Entities).Returns(this.mockEntityClient.Object);
        this.orchestrator = new ExecuteScheduleOperationOrchestrator();
    }

    [Fact]
    public async Task RunAsync_ValidRequest_CallsEntityOperation()
    {
        // Arrange
        var entityId = new EntityInstanceId(nameof(Schedule), "test-schedule");
        var operationName = "TestOperation";
        var input = new { TestData = "test" };
        var expectedResult = new { Result = "success" };
        var request = new ScheduleOperationRequest(entityId, operationName, input);

        this.mockEntityClient
            .Setup(e => e.CallEntityAsync<object>(entityId, operationName, input, default))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await this.orchestrator.RunAsync(this.mockContext.Object, request);

        // Assert
        Assert.Equal(expectedResult, result);
        this.mockEntityClient.Verify(
            e => e.CallEntityAsync<object>(entityId, operationName, input, default),
            Times.Once);
    }

    [Fact]
    public async Task RunAsync_NullInput_CallsEntityOperation()
    {
        // Arrange
        var entityId = new EntityInstanceId(nameof(Schedule), "test-schedule");
        var operationName = "TestOperation";
        var expectedResult = new { Result = "success" };
        var request = new ScheduleOperationRequest(entityId, operationName);

        this.mockEntityClient
            .Setup(e => e.CallEntityAsync<object>(entityId, operationName, null, default))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await this.orchestrator.RunAsync(this.mockContext.Object, request);

        // Assert
        Assert.Equal(expectedResult, result);
        this.mockEntityClient.Verify(
            e => e.CallEntityAsync<object>(entityId, operationName, null, default),
            Times.Once);
    }

    [Fact]
    public async Task RunAsync_EntityOperationFails_PropagatesException()
    {
        // Arrange
        var entityId = new EntityInstanceId(nameof(Schedule), "test-schedule");
        var operationName = "TestOperation";
        var request = new ScheduleOperationRequest(entityId, operationName);
        var expectedException = new InvalidOperationException("Test exception");

        this.mockEntityClient
            .Setup(e => e.CallEntityAsync<object>(entityId, operationName, null, default))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => this.orchestrator.RunAsync(this.mockContext.Object, request));
        Assert.Equal(expectedException.Message, exception.Message);
    }
} 