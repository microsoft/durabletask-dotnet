// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.DurableTask.ScheduledTasks.Tests.Client;

public class ScheduleClientImplTests
{
    readonly Mock<DurableTaskClient> durableTaskClient;
    readonly Mock<DurableEntityClient> entityClient;
    readonly Mock<ILogger> logger;
    readonly ScheduleClientImpl client;
    readonly string scheduleId = "test-schedule";

    public ScheduleClientImplTests()
    {
        this.durableTaskClient = new Mock<DurableTaskClient>("test", MockBehavior.Strict);
        this.entityClient = new Mock<DurableEntityClient>("test", MockBehavior.Strict);
        this.logger = new Mock<ILogger>(MockBehavior.Loose);
        this.durableTaskClient.Setup(x => x.Entities).Returns(this.entityClient.Object);
        this.client = new ScheduleClientImpl(this.durableTaskClient.Object, this.scheduleId, this.logger.Object);
    }

    [Fact]
    public void Constructor_WithNullClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new ScheduleClientImpl(null!, this.scheduleId, this.logger.Object));
        Assert.Equal("client", ex.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Constructor_WithInvalidScheduleId_ThrowsArgumentException(string invalidScheduleId)
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            new ScheduleClientImpl(this.durableTaskClient.Object, invalidScheduleId, this.logger.Object));
        Assert.Contains("scheduleId cannot be null or empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new ScheduleClientImpl(this.durableTaskClient.Object, this.scheduleId, null!));
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

        this.entityClient
            .Setup(c => c.GetEntityAsync<ScheduleState>(
                It.Is<EntityInstanceId>(id => id.Name == nameof(Schedule) && id.Key == this.scheduleId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityMetadata<ScheduleState>(new EntityInstanceId(nameof(Schedule), this.scheduleId), state));

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
            .ReturnsAsync(new OrchestrationMetadata(nameof(ExecuteScheduleOperationOrchestrator), instanceId));

        // Act
        await this.client.DeleteAsync();

        // Assert
        this.durableTaskClient.Verify(
            c => c.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(n => n.Name == nameof(ExecuteScheduleOperationOrchestrator)),
                It.Is<ScheduleOperationRequest>(r =>
                    r.EntityId.Name == nameof(Schedule) &&
                    r.EntityId.Key == this.scheduleId &&
                    r.OperationName == "delete"),
                It.IsAny<CancellationToken>()),
            Times.Once);
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
            .ReturnsAsync(new OrchestrationMetadata(nameof(ExecuteScheduleOperationOrchestrator), instanceId));

        // Act
        await this.client.PauseAsync();

        // Assert
        this.durableTaskClient.Verify(
            c => c.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(n => n.Name == nameof(ExecuteScheduleOperationOrchestrator)),
                It.Is<ScheduleOperationRequest>(r =>
                    r.EntityId.Name == nameof(Schedule) &&
                    r.EntityId.Key == this.scheduleId &&
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
            .ReturnsAsync(new OrchestrationMetadata(nameof(ExecuteScheduleOperationOrchestrator), instanceId));

        // Act
        await this.client.ResumeAsync();

        // Assert
        this.durableTaskClient.Verify(
            c => c.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(n => n.Name == nameof(ExecuteScheduleOperationOrchestrator)),
                It.Is<ScheduleOperationRequest>(r =>
                    r.EntityId.Name == nameof(Schedule) &&
                    r.EntityId.Key == this.scheduleId &&
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
            .ReturnsAsync(new OrchestrationMetadata(nameof(ExecuteScheduleOperationOrchestrator), instanceId));

        // Act
        await this.client.UpdateAsync(updateOptions);

        // Assert
        this.durableTaskClient.Verify(
            c => c.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(n => n.Name == nameof(ExecuteScheduleOperationOrchestrator)),
                It.Is<ScheduleOperationRequest>(r =>
                    r.EntityId.Name == nameof(Schedule) &&
                    r.EntityId.Key == this.scheduleId &&
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