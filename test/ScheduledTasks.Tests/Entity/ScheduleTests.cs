// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities.Tests;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.DurableTask.ScheduledTasks.Tests.Entity;

public class ScheduleTests
{
    readonly Schedule schedule;
    readonly string scheduleId = "test-schedule";
    readonly TestLogger logger;

    public ScheduleTests(ITestOutputHelper output)
    {
        this.logger = new TestLogger();
        this.schedule = new Schedule((ILogger<Schedule>)this.logger);
    }

    // Simple TestLogger implementation for capturing logs
    class TestLogger : ILogger<Schedule>
    {
        public List<(LogLevel Level, string Message)> Logs { get; } = new();

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
        var state = operation.State.GetState(typeof(ScheduleState));
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

        // assert after create
        var state = createOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        ScheduleState scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal(ScheduleStatus.Active, scheduleState.Status);

        // Pause first time
        TestEntityOperation pauseOperation = new TestEntityOperation(
            nameof(Schedule.PauseSchedule),
            createOperation.State,
            null);
        await this.schedule.RunAsync(pauseOperation);

        // assert after first pause
        var stateAfterFirstPause = pauseOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(stateAfterFirstPause);
        ScheduleState scheduleStateAfterFirstPause = Assert.IsType<ScheduleState>(stateAfterFirstPause);
        Assert.Equal(ScheduleStatus.Paused, scheduleStateAfterFirstPause.Status);

        // Pause second time
        TestEntityOperation pauseOperation2 = new TestEntityOperation(
            nameof(Schedule.PauseSchedule),
            pauseOperation.State,
            null);

        // Act & Assert
        await Assert.ThrowsAsync<ScheduleInvalidTransitionException>(() =>
            this.schedule.RunAsync(new TestEntityOperation(
                nameof(Schedule.PauseSchedule),
                pauseOperation2.State,
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

        // Assert initial state is active
        var stateAfterCreate = createOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(stateAfterCreate);
        ScheduleState scheduleStateAfterCreate = Assert.IsType<ScheduleState>(stateAfterCreate);
        Assert.Equal(ScheduleStatus.Active, scheduleStateAfterCreate.Status);

        // Pause
        TestEntityOperation pauseOperation = new TestEntityOperation(
            nameof(Schedule.PauseSchedule),
            createOperation.State,
            null);
        await this.schedule.RunAsync(pauseOperation);

        // Assert paused state
        var stateAfterPause = pauseOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(stateAfterPause);
        ScheduleState scheduleStateAfterPause = Assert.IsType<ScheduleState>(stateAfterPause);
        Assert.Equal(ScheduleStatus.Paused, scheduleStateAfterPause.Status);

        // Act
        TestEntityOperation resumeOperation = new TestEntityOperation(
            nameof(Schedule.ResumeSchedule),
            pauseOperation.State,
            null);
        await this.schedule.RunAsync(resumeOperation);

        // Assert resumed state
        var stateAfterResume = resumeOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(stateAfterResume);
        ScheduleState scheduleStateAfterResume = Assert.IsType<ScheduleState>(stateAfterResume);
        Assert.Equal(ScheduleStatus.Active, scheduleStateAfterResume.Status);
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

        // Assert initial state is active
        object? stateAfterCreate = createOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(stateAfterCreate);
        ScheduleState scheduleStateAfterCreate = Assert.IsType<ScheduleState>(stateAfterCreate);
        Assert.Equal(ScheduleStatus.Active, scheduleStateAfterCreate.Status);

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

        // Assert initial state
        object? stateAfterCreate = createOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(stateAfterCreate);
        ScheduleState scheduleStateAfterCreate = Assert.IsType<ScheduleState>(stateAfterCreate);
        Assert.Equal(TimeSpan.FromMinutes(5), scheduleStateAfterCreate.ScheduleConfiguration?.Interval);

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

        // Assert updated state
        object? stateAfterUpdate = updateOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(stateAfterUpdate);
        ScheduleState scheduleStateAfterUpdate = Assert.IsType<ScheduleState>(stateAfterUpdate);
        Assert.Equal(TimeSpan.FromMinutes(10), scheduleStateAfterUpdate.ScheduleConfiguration?.Interval);
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

        // Assert initial state
        object? stateAfterCreate = createOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(stateAfterCreate);
        ScheduleState scheduleStateAfterCreate = Assert.IsType<ScheduleState>(stateAfterCreate);
        Assert.Equal(TimeSpan.FromMinutes(5), scheduleStateAfterCreate.ScheduleConfiguration?.Interval);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            this.schedule.RunAsync(new TestEntityOperation(
                nameof(Schedule.UpdateSchedule),
                stateAfterCreate,
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

        // Assert initial state is active
        object? stateAfterCreate = createOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(stateAfterCreate);
        ScheduleState scheduleStateAfterCreate = Assert.IsType<ScheduleState>(stateAfterCreate);
        Assert.Equal(ScheduleStatus.Active, scheduleStateAfterCreate.Status);

        // Pause
        TestEntityOperation pauseOperation = new TestEntityOperation(
            nameof(Schedule.PauseSchedule),
            createOperation.State,
            null);
        await this.schedule.RunAsync(pauseOperation);

        // Assert paused state
        object? stateAfterPause = pauseOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(stateAfterPause);
        ScheduleState scheduleStateAfterPause = Assert.IsType<ScheduleState>(stateAfterPause);
        Assert.Equal(ScheduleStatus.Paused, scheduleStateAfterPause.Status);

        // run schedule op
        TestEntityOperation runOp = new TestEntityOperation(
                nameof(Schedule.RunSchedule),
                scheduleStateAfterPause,
                scheduleStateAfterPause.ExecutionToken);
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            this.schedule.RunAsync(runOp).AsTask());
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

        // Assert initial state
        object? stateAfterCreate = createOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(stateAfterCreate);
        ScheduleState scheduleStateAfterCreate = Assert.IsType<ScheduleState>(stateAfterCreate);
        Assert.Equal(ScheduleStatus.Active, scheduleStateAfterCreate.Status);
        string? initialToken = scheduleStateAfterCreate.ExecutionToken;

        // Act
        TestEntityOperation runOperation = new TestEntityOperation(
            nameof(Schedule.RunSchedule),
            createOperation.State,
            "invalid-token");
        await this.schedule.RunAsync(runOperation);

        // Assert state unchanged
        object? stateAfterRun = runOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(stateAfterRun);
        ScheduleState scheduleStateAfterRun = Assert.IsType<ScheduleState>(stateAfterRun);
        Assert.Equal(ScheduleStatus.Active, scheduleStateAfterRun.Status);
        Assert.Equal(initialToken, scheduleStateAfterRun.ExecutionToken);
        Assert.Null(scheduleStateAfterRun.LastRunAt);
    }
}