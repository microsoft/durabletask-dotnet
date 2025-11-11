// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.DurableTask.ScheduledTasks.Tests.Client;

public class DefaultScheduleClientTests
{
    readonly Mock<DurableTaskClient> durableTaskClient;
    readonly Mock<DurableEntityClient> entityClient;
    readonly ILogger logger;
    readonly DefaultScheduleClient client;
    readonly string scheduleId = "test-schedule";

    public DefaultScheduleClientTests()
    {
        this.durableTaskClient = new Mock<DurableTaskClient>("test");
        this.entityClient = new Mock<DurableEntityClient>("test");
        this.logger = new TestLogger();
        this.durableTaskClient.Setup(x => x.Entities).Returns(this.entityClient.Object);
        this.client = new DefaultScheduleClient(this.durableTaskClient.Object, this.scheduleId, this.logger);
    }

    [Fact]
    public void Constructor_WithNullClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() =>
            new DefaultScheduleClient(null!, this.scheduleId, this.logger));
        Assert.Equal("client", ex.ParamName);
    }

    [Theory]
    [InlineData(null, typeof(ArgumentNullException), "Value cannot be null")]
    [InlineData("", typeof(ArgumentException), "Parameter cannot be an empty string")]
    public void Constructor_WithInvalidScheduleId_ThrowsCorrectException(string? invalidScheduleId, Type expectedExceptionType, string expectedMessage)
    {
        // Act & Assert
        var ex = Assert.Throws(expectedExceptionType, () =>
            new DefaultScheduleClient(this.durableTaskClient.Object, invalidScheduleId!, this.logger));

        Assert.Contains(expectedMessage, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new DefaultScheduleClient(this.durableTaskClient.Object, this.scheduleId, null!));
        Assert.Equal("logger", ex.ParamName);
    }

    [Fact]
    public async Task DescribeAsync_WhenExists_ReturnsDescription()
    {
        // Arrange
        var state = new ScheduleState
        {
            Status = ScheduleStatus.Active,
            ScheduleConfiguration = new ScheduleConfiguration(this.scheduleId, "test-orchestration", TimeSpan.FromMinutes(5))
        };

        // create entity instance id
        var entityInstanceId = new EntityInstanceId(nameof(Schedule), this.scheduleId);
        // get key and id
        var key = entityInstanceId.Key;
        var id = entityInstanceId.Name;

        this.entityClient
            .Setup(c => c.GetEntityAsync<ScheduleState>(
                It.Is<EntityInstanceId>(id => id.Name == entityInstanceId.Name && id.Key == entityInstanceId.Key),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityMetadata<ScheduleState>(entityInstanceId, state));

        // Act
        var description = await this.client.DescribeAsync();

        // Assert
        Assert.NotNull(description);
        Assert.Equal(this.scheduleId, description.ScheduleId);
        Assert.Equal(state.Status, description.Status);
        Assert.Equal(state.ScheduleConfiguration.OrchestrationName, description.OrchestrationName);
    }

    [Fact]
    public async Task DescribeAsync_WhenNotExists_ThrowsScheduleNotFoundException()
    {
        // Arrange
        this.entityClient
            .Setup(c => c.GetEntityAsync<ScheduleState>(
                It.Is<EntityInstanceId>(id => id.Name == nameof(Schedule) && id.Key == this.scheduleId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntityMetadata<ScheduleState>)null!);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ScheduleNotFoundException>(() => this.client.DescribeAsync());
        Assert.Equal(this.scheduleId, ex.ScheduleId);
    }

    [Fact]
    public async Task DeleteAsync_ExecutesDeleteOperation()
    {
        // Arrange
        string instanceId = "test-instance";

        this.durableTaskClient
            .Setup(c => c.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(n => n.Name == nameof(ExecuteScheduleOperationOrchestrator)),
                It.IsAny<ScheduleOperationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(instanceId);

        this.durableTaskClient
            .Setup(c => c.WaitForInstanceCompletionAsync(instanceId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrchestrationMetadata(nameof(ExecuteScheduleOperationOrchestrator), instanceId)
            {
                RuntimeStatus = OrchestrationRuntimeStatus.Completed
            });

        // Act
        await this.client.DeleteAsync();

        // create entity instance id
        var entityInstanceId = new EntityInstanceId(nameof(Schedule), this.scheduleId);
        // Assert
        this.durableTaskClient.Verify(
            c => c.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(n => n.Name == nameof(ExecuteScheduleOperationOrchestrator)),
                It.Is<ScheduleOperationRequest>(r =>
                    r.EntityId.Name == entityInstanceId.Name &&
                    r.EntityId.Key == entityInstanceId.Key &&
                    r.OperationName == "delete"),
                It.IsAny<CancellationToken>()),  // Ensure all arguments match
            Times.Once);


        // Verify that we waited for completion
        this.durableTaskClient.Verify(
            c => c.WaitForInstanceCompletionAsync(instanceId, true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WhenOrchestrationFails_ThrowsException()
    {
        // Arrange
        string instanceId = "test-instance";
        string errorMessage = "Test error message";

        this.durableTaskClient
            .Setup(c => c.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(n => n.Name == nameof(ExecuteScheduleOperationOrchestrator)),
                It.IsAny<ScheduleOperationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(instanceId);

        this.durableTaskClient
            .Setup(c => c.WaitForInstanceCompletionAsync(instanceId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrchestrationMetadata(nameof(ExecuteScheduleOperationOrchestrator), instanceId)
            {
                RuntimeStatus = OrchestrationRuntimeStatus.Failed,
                FailureDetails = new TaskFailureDetails("TestError", errorMessage, null, null, null)
            });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => this.client.DeleteAsync());

        Assert.Contains($"Failed to delete schedule '{this.scheduleId}'", exception.Message);
        Assert.Contains(errorMessage, exception.Message);
    }

    [Fact]
    public async Task PauseAsync_ExecutesPauseOperation()
    {
        // Arrange
        string instanceId = "test-instance";

        this.durableTaskClient
            .Setup(c => c.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(n => n.Name == nameof(ExecuteScheduleOperationOrchestrator)),
                It.IsAny<ScheduleOperationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(instanceId);

        this.durableTaskClient
            .Setup(c => c.WaitForInstanceCompletionAsync(instanceId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrchestrationMetadata(nameof(ExecuteScheduleOperationOrchestrator), instanceId) {
                RuntimeStatus = OrchestrationRuntimeStatus.Completed
            });

        // Act
        await this.client.PauseAsync();

        // create entity instance id
        var entityInstanceId = new EntityInstanceId(nameof(Schedule), this.scheduleId);
        // Assert
        this.durableTaskClient.Verify(
            c => c.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(n => n.Name == nameof(ExecuteScheduleOperationOrchestrator)),
                It.Is<ScheduleOperationRequest>(r =>
                    r.EntityId.Name == entityInstanceId.Name &&
                    r.EntityId.Key == entityInstanceId.Key &&
                    r.OperationName == nameof(Schedule.PauseSchedule)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResumeAsync_ExecutesResumeOperation()
    {
        // Arrange
        string instanceId = "test-instance";

        this.durableTaskClient
            .Setup(c => c.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(n => n.Name == nameof(ExecuteScheduleOperationOrchestrator)),
                It.IsAny<ScheduleOperationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(instanceId);

        this.durableTaskClient
            .Setup(c => c.WaitForInstanceCompletionAsync(instanceId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrchestrationMetadata(nameof(ExecuteScheduleOperationOrchestrator), instanceId) {
                RuntimeStatus = OrchestrationRuntimeStatus.Completed
            });

        // Act
        await this.client.ResumeAsync();

        // create entity instance id
        var entityInstanceId = new EntityInstanceId(nameof(Schedule), this.scheduleId);
        // Assert
        this.durableTaskClient.Verify(
            c => c.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(n => n.Name == nameof(ExecuteScheduleOperationOrchestrator)),
                It.Is<ScheduleOperationRequest>(r =>
                    r.EntityId.Name == entityInstanceId.Name &&
                    r.EntityId.Key == entityInstanceId.Key &&
                    r.OperationName == nameof(Schedule.ResumeSchedule)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_ExecutesUpdateOperation()
    {
        // Arrange
        string instanceId = "test-instance";
        var updateOptions = new ScheduleUpdateOptions
        {
            OrchestrationName = "new-orchestration",
            Interval = TimeSpan.FromMinutes(10)
        };

        this.durableTaskClient
            .Setup(c => c.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(n => n.Name == nameof(ExecuteScheduleOperationOrchestrator)),
                It.IsAny<ScheduleOperationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(instanceId);

        this.durableTaskClient
            .Setup(c => c.WaitForInstanceCompletionAsync(instanceId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrchestrationMetadata(nameof(ExecuteScheduleOperationOrchestrator), instanceId) {
                RuntimeStatus = OrchestrationRuntimeStatus.Completed
            });

        // Act
        await this.client.UpdateAsync(updateOptions);

        // create entity instance id
        var entityInstanceId = new EntityInstanceId(nameof(Schedule), this.scheduleId);
        // Assert
        this.durableTaskClient.Verify(
            c => c.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(n => n.Name == nameof(ExecuteScheduleOperationOrchestrator)),
                It.Is<ScheduleOperationRequest>(r =>
                    r.EntityId.Name == entityInstanceId.Name &&
                    r.EntityId.Key == entityInstanceId.Key &&
                    r.OperationName == nameof(Schedule.UpdateSchedule)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WithNullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => this.client.UpdateAsync(null!));
        Assert.Equal("updateOptions", ex.ParamName);
    }
}