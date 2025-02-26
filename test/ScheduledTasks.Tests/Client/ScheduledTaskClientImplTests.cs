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
    readonly Mock<DurableTaskClient> durableTaskClient;
    readonly Mock<DurableEntityClient> entityClient;
    readonly ILogger logger;
    readonly ScheduledTaskClientImpl client;

    public ScheduledTaskClientImplTests()
    {
        this.durableTaskClient = new Mock<DurableTaskClient>("test");
        this.entityClient = new Mock<DurableEntityClient>("test");
        this.logger = new TestLogger();
        this.durableTaskClient.Setup(x => x.Entities).Returns(this.entityClient.Object);
        this.client = new ScheduledTaskClientImpl(this.durableTaskClient.Object, this.logger);
    }

    [Fact]
    public void Constructor_WithNullClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new ScheduledTaskClientImpl(null!, this.logger));
        Assert.Equal("durableTaskClient", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new ScheduledTaskClientImpl(this.durableTaskClient.Object, null!));
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
    [InlineData(null, typeof(ArgumentNullException), "Value cannot be null")]
    [InlineData("", typeof(ArgumentException), "Parameter cannot be an empty string")]
    public void GetScheduleClient_WithInvalidId_ThrowsCorrectException(string scheduleId, Type expectedExceptionType, string expectedMessage)
    {
        // Act & Assert
        var ex = Assert.Throws(expectedExceptionType, () => this.client.GetScheduleClient(scheduleId));
        Assert.Contains(expectedMessage, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateScheduleAsync_WithValidOptions_CreatesSchedule()
    {
        // Arrange
        var options = new ScheduleCreationOptions("test-schedule", "test-orchestration", TimeSpan.FromMinutes(5));
        string instanceId = "test-instance";

        // create entity instance id
        var entityInstanceId = new EntityInstanceId(nameof(Schedule), options.ScheduleId);

        this.durableTaskClient
            .Setup(x => x.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(n => n.Name == nameof(ExecuteScheduleOperationOrchestrator)),
                It.Is<ScheduleOperationRequest>(r =>
                    r.EntityId.Name == entityInstanceId.Name &&
                    r.EntityId.Key == entityInstanceId.Key &&
                    r.OperationName == nameof(Schedule.CreateSchedule) &&
                    r.Input != null && ((ScheduleCreationOptions)r.Input).Equals(options)),
                null,
                default))
            .ReturnsAsync(instanceId);

        this.durableTaskClient
            .Setup(x => x.WaitForInstanceCompletionAsync(instanceId, true, default))
            .ReturnsAsync(new OrchestrationMetadata(nameof(ExecuteScheduleOperationOrchestrator), instanceId)
            {
                RuntimeStatus = OrchestrationRuntimeStatus.Completed
            });

        // Act
        var scheduleClient = await this.client.CreateScheduleAsync(options);

        // Assert
        Assert.NotNull(scheduleClient);
        Assert.Equal(options.ScheduleId, scheduleClient.ScheduleId);

        this.durableTaskClient.Verify(
            x => x.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(n => n.Name == nameof(ExecuteScheduleOperationOrchestrator)),
                It.Is<ScheduleOperationRequest>(r =>
                    r.EntityId.Name == nameof(Schedule) &&
                    r.EntityId.Key == options.ScheduleId &&
                    r.OperationName == nameof(Schedule.CreateSchedule) &&
                    r.Input != null && ((ScheduleCreationOptions)r.Input).Equals(options)),
                null,
                default),
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

        this.entityClient
            .Setup(x => x.GetEntityAsync<ScheduleState>(
                It.Is<EntityInstanceId>(id => id.Name == nameof(Schedule) && id.Key == scheduleId),
                true,
                default))
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

        this.entityClient
            .Setup(x => x.GetEntityAsync<ScheduleState>(
                It.Is<EntityInstanceId>(id => id.Name == nameof(Schedule) && id.Key == scheduleId),
                true,
                default))
            .ReturnsAsync((EntityMetadata<ScheduleState?>)null);

        // Act
        var description = await this.client.GetScheduleAsync(scheduleId);

        // Assert
        Assert.Null(description);
    }

    [Fact]
    public async Task ListSchedulesAsync_ReturnsSchedules()
    {
        // Arrange
        var query = new ScheduleQuery
        {
            ScheduleIdPrefix = "test",
            Status = ScheduleStatus.Active,
            PageSize = 10
        };

        var states = new[]
        {
            new EntityMetadata<ScheduleState>(
                new EntityInstanceId(nameof(Schedule), "test-1"),
                new ScheduleState
                {
                    Status = ScheduleStatus.Active,
                    ScheduleConfiguration = new ScheduleConfiguration("test-1", "test-orchestration", TimeSpan.FromMinutes(5))
                }),
            new EntityMetadata<ScheduleState>(
                new EntityInstanceId(nameof(Schedule), "test-2"),
                new ScheduleState
                {
                    Status = ScheduleStatus.Active,
                    ScheduleConfiguration = new ScheduleConfiguration("test-2", "test-orchestration", TimeSpan.FromMinutes(5))
                })
        };

        this.entityClient
            .Setup(x => x.GetAllEntitiesAsync<ScheduleState>(It.IsAny<EntityQuery>()))
            .Returns(Pageable.Create<EntityMetadata<ScheduleState>>((continuation, pageSize, cancellation) =>
                Task.FromResult(new Page<EntityMetadata<ScheduleState>>(states.ToList(), null))));

        // Act
        var schedules = new List<ScheduleDescription>();
        await foreach (var schedule in this.client.ListSchedulesAsync(query))
        {
            schedules.Add(schedule);
        }

        // Assert
        Assert.Equal(2, schedules.Count);
        Assert.All(schedules, s => Assert.StartsWith("test-", s.ScheduleId));
        Assert.All(schedules, s => Assert.Equal(ScheduleStatus.Active, s.Status));
    }
}