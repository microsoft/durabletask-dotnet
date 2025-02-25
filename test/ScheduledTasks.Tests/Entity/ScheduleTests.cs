// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.DurableTask.ScheduledTasks.Tests.Entity;

public class ScheduleTests
{
    readonly Mock<ILogger<Schedule>> mockLogger;
    readonly Mock<TaskEntityContext> mockContext;
    readonly Schedule schedule;
    readonly string scheduleId = "test-schedule";

    public ScheduleTests()
    {
        this.mockLogger = new Mock<ILogger<Schedule>>(MockBehavior.Loose);
        this.mockContext = new Mock<TaskEntityContext>(MockBehavior.Strict);
        this.schedule = new Schedule(this.mockLogger.Object);
    }

    [Fact]
    public void CreateSchedule_WithValidOptions_CreatesSchedule()
    {
        // Arrange
        var options = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        // Act
        this.schedule.CreateSchedule(this.mockContext.Object, options);

        // Assert
        this.mockContext.Verify(c => c.SignalEntity(
            It.Is<EntityInstanceId>(id => id.Name == nameof(Schedule) && id.Key == this.scheduleId),
            nameof(Schedule.RunSchedule),
            It.IsAny<string>(),
            null), Times.Once);
    }

    [Fact]
    public void CreateSchedule_WithNullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ScheduleClientValidationException>(() =>
            this.schedule.CreateSchedule(this.mockContext.Object, null!));
        Assert.Contains("Schedule creation options cannot be null", ex.Message);
    }

    [Fact]
    public void PauseSchedule_WhenActive_PausesSchedule()
    {
        // Arrange
        var options = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));
        this.schedule.CreateSchedule(this.mockContext.Object, options);

        // Act
        this.schedule.PauseSchedule(this.mockContext.Object);

        // Assert
        this.mockContext.Verify(c => c.SignalEntity(
            It.Is<EntityInstanceId>(id => id.Name == nameof(Schedule) && id.Key == this.scheduleId),
            nameof(Schedule.RunSchedule),
            It.IsAny<string>(),
            null), Times.Once);
    }

    [Fact]
    public void PauseSchedule_WhenAlreadyPaused_ThrowsInvalidTransitionException()
    {
        // Arrange
        var options = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));
        this.schedule.CreateSchedule(this.mockContext.Object, options);
        this.schedule.PauseSchedule(this.mockContext.Object);

        // Act & Assert
        var ex = Assert.Throws<ScheduleInvalidTransitionException>(() =>
            this.schedule.PauseSchedule(this.mockContext.Object));
    }

    [Fact]
    public void ResumeSchedule_WhenPaused_ResumesSchedule()
    {
        // Arrange
        var options = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));
        this.schedule.CreateSchedule(this.mockContext.Object, options);
        this.schedule.PauseSchedule(this.mockContext.Object);

        // Act
        this.schedule.ResumeSchedule(this.mockContext.Object);

        // Assert
        this.mockContext.Verify(c => c.SignalEntity(
            It.Is<EntityInstanceId>(id => id.Name == nameof(Schedule) && id.Key == this.scheduleId),
            nameof(Schedule.RunSchedule),
            It.IsAny<string>(),
            null), Times.Exactly(2));
    }

    [Fact]
    public void ResumeSchedule_WhenActive_ThrowsInvalidTransitionException()
    {
        // Arrange
        var options = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));
        this.schedule.CreateSchedule(this.mockContext.Object, options);

        // Act & Assert
        var ex = Assert.Throws<ScheduleInvalidTransitionException>(() =>
            this.schedule.ResumeSchedule(this.mockContext.Object));
    }

    [Fact]
    public void UpdateSchedule_WithValidOptions_UpdatesSchedule()
    {
        // Arrange
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));
        this.schedule.CreateSchedule(this.mockContext.Object, createOptions);

        var updateOptions = new ScheduleUpdateOptions
        {
            Interval = TimeSpan.FromMinutes(10)
        };

        // Act
        this.schedule.UpdateSchedule(this.mockContext.Object, updateOptions);

        // Assert
        this.mockContext.Verify(c => c.SignalEntity(
            It.Is<EntityInstanceId>(id => id.Name == nameof(Schedule) && id.Key == this.scheduleId),
            nameof(Schedule.RunSchedule),
            It.IsAny<string>(),
            null), Times.Once);
    }

    [Fact]
    public void UpdateSchedule_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));
        this.schedule.CreateSchedule(this.mockContext.Object, createOptions);

        // Act & Assert
        var ex = Assert.Throws<ScheduleClientValidationException>(() =>
            this.schedule.UpdateSchedule(this.mockContext.Object, null!));
        Assert.Contains("Schedule update options cannot be null", ex.Message);
    }

    [Fact]
    public void RunSchedule_WhenNotActive_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));
        this.schedule.CreateSchedule(this.mockContext.Object, options);
        this.schedule.PauseSchedule(this.mockContext.Object);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            this.schedule.RunSchedule(this.mockContext.Object, "token"));
        Assert.Contains("Schedule must be in Active status to run", ex.Message);
    }

    [Fact]
    public void RunSchedule_WithInvalidToken_DoesNotRun()
    {
        // Arrange
        var options = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));
        this.schedule.CreateSchedule(this.mockContext.Object, options);

        // Act
        this.schedule.RunSchedule(this.mockContext.Object, "invalid-token");

        // Assert
        this.mockContext.Verify(c => c.ScheduleNewOrchestration(
            It.IsAny<TaskName>(),
            It.IsAny<object>(),
            It.IsAny<StartOrchestrationOptions>()), Times.Never);
    }
}