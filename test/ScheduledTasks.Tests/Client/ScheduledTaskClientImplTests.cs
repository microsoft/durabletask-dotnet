// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.DurableTask.ScheduledTasks.Tests.Client;

public class ScheduledTaskClientImplTests
{
    readonly Mock<DurableTaskClient> durableTaskClientMock;
    readonly Mock<ILogger> loggerMock;
    readonly Mock<DurableEntityClient> entityClientMock;
    readonly ScheduledTaskClientImpl client;

    public ScheduledTaskClientImplTests()
    {
        this.durableTaskClientMock = new Mock<DurableTaskClient>();
        this.loggerMock = new Mock<ILogger>();
        this.entityClientMock = new Mock<DurableEntityClient>();
        this.durableTaskClientMock.Setup(c => c.Entities).Returns(this.entityClientMock.Object);
        this.client = new ScheduledTaskClientImpl(this.durableTaskClientMock.Object, this.loggerMock.Object);
    }

    [Fact]
    public void Constructor_WithNullClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new ScheduledTaskClientImpl(null!, this.loggerMock.Object));
        Assert.Equal("durableTaskClient", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new ScheduledTaskClientImpl(this.durableTaskClientMock.Object, null!));
        Assert.Equal("logger", ex.ParamName);
    }

    [Fact]
    public void GetScheduleClient_ReturnsValidClient()
    {
        // Arrange
        string scheduleId = "test-schedule";

        // Act
        var scheduleClient = this.client.GetScheduleClient(scheduleId);

        // Assert
        Assert.NotNull(scheduleClient);
        Assert.Equal(scheduleId, scheduleClient.ScheduleId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void GetScheduleClient_WithInvalidId_ThrowsArgumentException(string scheduleId)
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => this.client.GetScheduleClient(scheduleId));
        Assert.Contains("scheduleId cannot be null or empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateScheduleAsync_WithValidOptions_CreatesSchedule()
    {
        // Arrange
        var options = new ScheduleCreationOptions("test-schedule", "test-orchestration", TimeSpan.FromMinutes(5));
        string instanceId = "test-instance";

        this.durableTaskClientMock
            .Setup(c => c.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(n => n.Name == nameof(ExecuteScheduleOperationOrchestrator)),
                It.IsAny<ScheduleOperationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(instanceId);

        this.durableTaskClientMock
            .Setup(c => c.WaitForInstanceCompletionAsync(instanceId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrchestrationMetadata(nameof(ExecuteScheduleOperationOrchestrator), instanceId));

        // Act
        var scheduleClient = await this.client.CreateScheduleAsync(options);

        // Assert
        Assert.NotNull(scheduleClient);
        Assert.Equal(options.ScheduleId, scheduleClient.ScheduleId);

        this.durableTaskClientMock.Verify(
            c => c.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(n => n.Name == nameof(ExecuteScheduleOperationOrchestrator)),
                It.Is<ScheduleOperationRequest>(r =>
                    r.EntityId.Name == nameof(Schedule) &&
                    r.EntityId.Key == options.ScheduleId &&
                    r.OperationName == nameof(Schedule.CreateSchedule)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateScheduleAsync_WithNullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => this.client.CreateScheduleAsync(null!));
        Assert.Equal("creationOptions", ex.ParamName);
    }

    [Fact]
    public async Task GetScheduleAsync_WhenExists_ReturnsDescription()
    {
        // Arrange
        string scheduleId = "test-schedule";
        var state = new ScheduleState
        {
            Status = ScheduleStatus.Active,
            ScheduleConfiguration = new ScheduleConfiguration(scheduleId, "test-orchestration", TimeSpan.FromMinutes(5))
        };

        this.entityClientMock
            .Setup(c => c.GetEntityAsync<ScheduleState>(
                It.Is<EntityInstanceId>(id => id.Name == nameof(Schedule) && id.Key == scheduleId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityMetadata<ScheduleState>(new EntityInstanceId(nameof(Schedule), scheduleId), state));

        // Act
        var description = await this.client.GetScheduleAsync(scheduleId);

        // Assert
        Assert.NotNull(description);
        Assert.Equal(scheduleId, description.ScheduleId);
        Assert.Equal(state.Status, description.Status);
        Assert.Equal(state.ScheduleConfiguration.OrchestrationName, description.OrchestrationName);
    }

    [Fact]
    public async Task GetScheduleAsync_WhenNotExists_ReturnsNull()
    {
        // Arrange
        string scheduleId = "test-schedule";

        this.entityClientMock
            .Setup(c => c.GetEntityAsync<ScheduleState>(
                It.Is<EntityInstanceId>(id => id.Name == nameof(Schedule) && id.Key == scheduleId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntityMetadata<ScheduleState>)null!);

        // Act
        var description = await this.client.GetScheduleAsync(scheduleId);

        // Assert
        Assert.Null(description);
    }
}