// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities.Tests;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.DurableTask.ScheduledTasks.Tests.Entity;

public class ScheduleTests
{
    private readonly Schedule schedule;
    private readonly string scheduleId = "test-schedule";
    private readonly TestLogger logger;

    public ScheduleTests(ITestOutputHelper output)
    {
        this.logger = new TestLogger();
        this.schedule = new Schedule((ILogger<Schedule>)this.logger);
    }

    // Simple TestLogger implementation for capturing logs
    private class TestLogger : ILogger<Schedule>
    {
        public List<(LogLevel Level, string Message)> Logs { get; } = new List<(LogLevel, string)>();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            string message = formatter(state, exception);
            this.Logs.Add((logLevel, message));
        }
    }

    [Fact]
    public async Task CreateSchedule_WithValidOptions_CreatesSchedule()
    {
        // Arrange
        ScheduleCreationOptions options = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        // Create test operation
        TestEntityOperation operation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            options);

        // Act
        await this.schedule.RunAsync(operation);

        // Assert
        object? state = operation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        ScheduleState scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.NotNull(scheduleState.ScheduleConfiguration);
        Assert.Equal(this.scheduleId, scheduleState.ScheduleConfiguration.ScheduleId);
        Assert.Equal("TestOrchestration", scheduleState.ScheduleConfiguration.OrchestrationName);
        Assert.Equal(TimeSpan.FromMinutes(5), scheduleState.ScheduleConfiguration.Interval);
        Assert.Equal(ScheduleStatus.Active, scheduleState.Status);
    }

    [Fact]
    public async Task PauseSchedule_WhenAlreadyPaused_ThrowsInvalidTransitionException()
    {
        // Arrange
        ScheduleCreationOptions createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        // Create initial state
        TestEntityOperation createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        // Pause first time
        TestEntityOperation pauseOperation = new TestEntityOperation(
            nameof(Schedule.PauseSchedule),
            createOperation.State,
            null);
        await this.schedule.RunAsync(pauseOperation);

        // Act & Assert
        await Assert.ThrowsAsync<ScheduleInvalidTransitionException>(() =>
            this.schedule.RunAsync(new TestEntityOperation(
                nameof(Schedule.PauseSchedule),
                pauseOperation.State,
                null)).AsTask());
    }

    [Fact]
    public async Task ResumeSchedule_WhenPaused_ResumesSchedule()
    {
        // Arrange
        ScheduleCreationOptions createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        // Create initial state
        TestEntityOperation createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        // Pause
        TestEntityOperation pauseOperation = new TestEntityOperation(
            nameof(Schedule.PauseSchedule),
            createOperation.State,
            null);
        await this.schedule.RunAsync(pauseOperation);

        // Act
        TestEntityOperation resumeOperation = new TestEntityOperation(
            nameof(Schedule.ResumeSchedule),
            pauseOperation.State,
            null);
        await this.schedule.RunAsync(resumeOperation);

        // Assert
    }

    [Fact]
    public async Task ResumeSchedule_WhenActive_ThrowsInvalidTransitionException()
    {
        // Arrange
        ScheduleCreationOptions createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        // Create initial state
        TestEntityOperation createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        // Act & Assert
        await Assert.ThrowsAsync<ScheduleInvalidTransitionException>(() =>
            this.schedule.RunAsync(new TestEntityOperation(
                nameof(Schedule.ResumeSchedule),
                createOperation.State,
                null)).AsTask());
    }

    [Fact]
    public async Task UpdateSchedule_WithValidOptions_UpdatesSchedule()
    {
        // Arrange
        ScheduleCreationOptions createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        // Create initial state
        TestEntityOperation createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        ScheduleUpdateOptions updateOptions = new ScheduleUpdateOptions
        {
            Interval = TimeSpan.FromMinutes(10)
        };

        // Act
        TestEntityOperation updateOperation = new TestEntityOperation(
            nameof(Schedule.UpdateSchedule),
            createOperation.State,
            updateOptions);
        await this.schedule.RunAsync(updateOperation);

        // Assert
    }

    [Fact]
    public async Task UpdateSchedule_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        ScheduleCreationOptions createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        // Create initial state
        TestEntityOperation createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        // Act & Assert
        await Assert.ThrowsAsync<ScheduleClientValidationException>(() =>
            this.schedule.RunAsync(new TestEntityOperation(
                nameof(Schedule.UpdateSchedule),
                createOperation.State,
                null)).AsTask());
    }

    [Fact]
    public async Task RunSchedule_WhenNotActive_ThrowsInvalidOperationException()
    {
        // Arrange
        ScheduleCreationOptions createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        // Create initial state
        TestEntityOperation createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        // Pause
        TestEntityOperation pauseOperation = new TestEntityOperation(
            nameof(Schedule.PauseSchedule),
            createOperation.State,
            null);
        await this.schedule.RunAsync(pauseOperation);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            this.schedule.RunAsync(new TestEntityOperation(
                nameof(Schedule.RunSchedule),
                pauseOperation.State,
                "token")).AsTask());
    }

    [Fact]
    public async Task RunSchedule_WithInvalidToken_DoesNotRun()
    {
        // Arrange
        ScheduleCreationOptions createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        // Create initial state
        TestEntityOperation createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        // Act
        await this.schedule.RunAsync(new TestEntityOperation(
            nameof(Schedule.RunSchedule),
            createOperation.State,
            "invalid-token"));

        // Assert
    }
}