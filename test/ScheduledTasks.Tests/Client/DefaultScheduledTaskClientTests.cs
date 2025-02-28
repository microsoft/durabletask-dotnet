// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.DurableTask.ScheduledTasks.Tests.Client;

public class DefaultScheduledTaskClientTests
{
    readonly Mock<DurableTaskClient> durableTaskClient;
    readonly Mock<DurableEntityClient> entityClient;
    readonly ILogger logger;
    readonly DefaultScheduledTaskClient client;

    public DefaultScheduledTaskClientTests()
    {
        this.durableTaskClient = new Mock<DurableTaskClient>("test");
        this.entityClient = new Mock<DurableEntityClient>("test");
        this.logger = new TestLogger();
        this.durableTaskClient.Setup(x => x.Entities).Returns(this.entityClient.Object);
        this.client = new DefaultScheduledTaskClient(this.durableTaskClient.Object, this.logger);
    }

    [Fact]
    public void Constructor_WithNullClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new DefaultScheduledTaskClient(null!, this.logger));
        Assert.Equal("durableTaskClient", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new DefaultScheduledTaskClient(this.durableTaskClient.Object, null!));
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
                It.IsAny<ScheduleOperationRequest>(),
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
                    r.EntityId.Name == entityInstanceId.Name &&
                    r.EntityId.Key == entityInstanceId.Key &&
                    r.OperationName == nameof(Schedule.CreateSchedule) &&
                    ((ScheduleCreationOptions)r.Input).ScheduleId == options.ScheduleId &&
                    ((ScheduleCreationOptions)r.Input).OrchestrationName == options.OrchestrationName &&
                    ((ScheduleCreationOptions)r.Input).Interval == options.Interval),
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
        // create now
        var now = DateTimeOffset.UtcNow;
        string scheduleId = "test-schedule";
        var config = new ScheduleConfiguration(scheduleId, "test-orchestration", TimeSpan.FromMinutes(5))
        {
            StartAt = now.AddMinutes(-10),
            EndAt = now.AddDays(1),
            StartImmediatelyIfLate = true,
            OrchestrationInput = "test-input",
            OrchestrationInstanceId = "test-instance"
        };

        var state = new ScheduleState
        {
            Status = ScheduleStatus.Active,
            ScheduleConfiguration = config,
            ExecutionToken = "test-token",
            LastRunAt = now.AddMinutes(-5),
            NextRunAt = now.AddMinutes(5),
            ScheduleCreatedAt = now.AddDays(-1)
        };

        var metadata = new EntityMetadata<ScheduleState>(
            new EntityInstanceId(nameof(Schedule), scheduleId),
            state);

        this.durableTaskClient
            .Setup(x => x.Entities)
            .Returns(this.entityClient.Object);

        // create entity instance id
        var entityInstanceId = new EntityInstanceId(nameof(Schedule), scheduleId);

        this.entityClient
            .Setup(x => x.GetEntityAsync<ScheduleState>(
                It.Is<EntityInstanceId>(id => id.Name == entityInstanceId.Name && id.Key == entityInstanceId.Key),
                default))
            .ReturnsAsync(metadata);

        // Act
        var description = await this.client.GetScheduleAsync(scheduleId);

        //verify getentityasync is called
        this.entityClient.Verify(x => x.GetEntityAsync<ScheduleState>(entityInstanceId, default), Times.Once);

        // Assert
        Assert.NotNull(description);
        Assert.Equal(scheduleId, description.ScheduleId);
        Assert.Equal(ScheduleStatus.Active, description.Status);
        Assert.Equal(config.OrchestrationName, description.OrchestrationName);
        Assert.Equal(config.OrchestrationInput, description.OrchestrationInput);
        Assert.Equal(config.OrchestrationInstanceId, description.OrchestrationInstanceId);
        Assert.Equal(config.StartAt, description.StartAt);
        Assert.Equal(config.EndAt, description.EndAt);
        Assert.Equal(config.Interval, description.Interval);
        Assert.Equal(config.StartImmediatelyIfLate, description.StartImmediatelyIfLate);
        Assert.Equal("test-token", description.ExecutionToken);
        Assert.Equal(now.AddMinutes(-5), description.LastRunAt);
        Assert.Equal(now.AddMinutes(5), description.NextRunAt);
    }

    [Fact]
    public async Task GetScheduleAsync_WhenNotExists_ReturnsNull()
    {
        // Arrange
        string scheduleId = "test-schedule";

        // create entity instance id
        var entityInstanceId = new EntityInstanceId(nameof(Schedule), scheduleId);

        this.entityClient
            .Setup(x => x.GetEntityAsync<ScheduleState>(
                It.Is<EntityInstanceId>(id => id.Name == entityInstanceId.Name && id.Key == entityInstanceId.Key),
                true,
                default))
            .ReturnsAsync((EntityMetadata<ScheduleState>?)null);

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
                    {
                        StartAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                        EndAt = DateTimeOffset.UtcNow.AddDays(1),
                        StartImmediatelyIfLate = true,
                        OrchestrationInput = "test-input-1",
                        OrchestrationInstanceId = "test-instance-1"
                    },
                    ExecutionToken = "test-token-1",
                    LastRunAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                    NextRunAt = DateTimeOffset.UtcNow.AddMinutes(5),
                    ScheduleCreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
                }),
            new EntityMetadata<ScheduleState>(
                new EntityInstanceId(nameof(Schedule), "test-2"),
                new ScheduleState
                {
                    Status = ScheduleStatus.Active,
                    ScheduleConfiguration = new ScheduleConfiguration("test-2", "test-orchestration", TimeSpan.FromMinutes(5))
                    {
                        StartAt = DateTimeOffset.UtcNow.AddMinutes(-8),
                        EndAt = DateTimeOffset.UtcNow.AddDays(2),
                        StartImmediatelyIfLate = true,
                        OrchestrationInput = "test-input-2",
                        OrchestrationInstanceId = "test-instance-2"
                    },
                    ExecutionToken = "test-token-2",
                    LastRunAt = DateTimeOffset.UtcNow.AddMinutes(-3),
                    NextRunAt = DateTimeOffset.UtcNow.AddMinutes(7),
                    ScheduleCreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
                })
        };

        this.durableTaskClient
            .Setup(x => x.Entities)
            .Returns(this.entityClient.Object);

        this.entityClient
            .Setup(x => x.GetAllEntitiesAsync<ScheduleState>(
                It.IsAny<EntityQuery>()))
            .Returns(Pageable.Create<EntityMetadata<ScheduleState>>((continuation, pageSize, cancellation) =>
            {
                var page = new Page<EntityMetadata<ScheduleState>>(states, continuation);
                return Task.FromResult(page);
            }));

        // Act
        var schedules = new List<ScheduleDescription>();
        await foreach (var schedule in this.client.ListSchedulesAsync(query))
        {
            schedules.Add(schedule);
        }

        // Assert
        // verify getallentitiesasync is called
        this.entityClient.Verify(x => x.GetAllEntitiesAsync<ScheduleState>(
            It.Is<EntityQuery>(q =>
                q.InstanceIdStartsWith == $"@schedule@test" &&
                q.IncludeState == true &&
                q.PageSize == query.PageSize)), Times.Once);

        Assert.Equal(2, schedules.Count);
        Assert.All(schedules, s => Assert.StartsWith("test-", s.ScheduleId));
        Assert.All(schedules, s => Assert.Equal(ScheduleStatus.Active, s.Status));
        Assert.All(schedules, s => Assert.NotNull(s.ExecutionToken));
        Assert.All(schedules, s => Assert.NotNull(s.LastRunAt));
        Assert.All(schedules, s => Assert.NotNull(s.NextRunAt));
    }
}