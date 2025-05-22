// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Dapr.DurableTask.Entities.Tests;
using Xunit;
using Xunit.Abstractions;

namespace Dapr.DurableTask.ScheduledTasks.Tests.Entity;

public class ScheduleTests
{
    readonly Schedule schedule;
    readonly string scheduleId = "test-schedule";
    readonly TestLogger<Schedule> logger;

    public ScheduleTests(ITestOutputHelper output)
    {
        this.logger = new TestLogger<Schedule>();
        this.schedule = new Schedule(this.logger);
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
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            this.schedule.RunAsync(runOp).AsTask());
        // check exception message Schedule must be in Active status to run.
        Assert.Contains("Schedule must be in Active status to run.", exception.Message);
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

    [Fact]
    public async Task CreateSchedule_WithStartAt_SetsNextRunTimeCorrectly()
    {
        // Arrange
        var startAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var options = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5))
        {
            StartAt = startAt
        };

        // Create test operation
        var operation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            options);

        // Act
        await this.schedule.RunAsync(operation);

        // Assert
        var state = operation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal(startAt, scheduleState.ScheduleConfiguration?.StartAt);
        Assert.Equal(ScheduleStatus.Active, scheduleState.Status);
    }

    [Fact]
    public async Task CreateSchedule_WithEndAt_SetsEndTimeCorrectly()
    {
        // Arrange
        var endAt = DateTimeOffset.UtcNow.AddHours(1);
        var options = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5))
        {
            EndAt = endAt
        };

        // Create test operation
        var operation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            options);

        // Act
        await this.schedule.RunAsync(operation);

        // Assert
        var state = operation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal(endAt, scheduleState.ScheduleConfiguration?.EndAt);
    }

    [Fact]
    public async Task CreateSchedule_WithStartImmediatelyIfLate_SetsPropertyCorrectly()
    {
        // Arrange
        var options = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5))
        {
            StartImmediatelyIfLate = true
        };

        // Create test operation
        var operation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            options);

        // Act
        await this.schedule.RunAsync(operation);

        // Assert
        var state = operation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.True(scheduleState.ScheduleConfiguration?.StartImmediatelyIfLate);
    }

    [Fact]
    public async Task UpdateSchedule_WithStartAt_UpdatesStartTimeCorrectly()
    {
        // Arrange
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        var createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        var newStartAt = DateTimeOffset.UtcNow.AddMinutes(10);
        var updateOptions = new ScheduleUpdateOptions
        {
            StartAt = newStartAt
        };

        // Act
        var updateOperation = new TestEntityOperation(
            nameof(Schedule.UpdateSchedule),
            createOperation.State,
            updateOptions);
        await this.schedule.RunAsync(updateOperation);

        // Assert
        var state = updateOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal(newStartAt, scheduleState.ScheduleConfiguration?.StartAt);
        Assert.Null(scheduleState.NextRunAt); // NextRunAt should be reset
    }

    [Fact]
    public async Task UpdateSchedule_WithEndAt_UpdatesEndTimeCorrectly()
    {
        // Arrange
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        var createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        var newEndAt = DateTimeOffset.UtcNow.AddHours(2);
        var updateOptions = new ScheduleUpdateOptions
        {
            EndAt = newEndAt
        };

        // Act
        var updateOperation = new TestEntityOperation(
            nameof(Schedule.UpdateSchedule),
            createOperation.State,
            updateOptions);
        await this.schedule.RunAsync(updateOperation);

        // Assert
        var state = updateOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal(newEndAt, scheduleState.ScheduleConfiguration?.EndAt);
    }

    [Fact]
    public async Task UpdateSchedule_WithNoChanges_DoesNotUpdateState()
    {
        // Arrange
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        var createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        var initialState = createOperation.State.GetState(typeof(ScheduleState));
        var initialScheduleState = Assert.IsType<ScheduleState>(initialState);
        var initialModifiedTime = initialScheduleState.ScheduleLastModifiedAt;

        var updateOptions = new ScheduleUpdateOptions
        {
            Interval = TimeSpan.FromMinutes(5) // Same interval
        };

        // Act
        var updateOperation = new TestEntityOperation(
            nameof(Schedule.UpdateSchedule),
            createOperation.State,
            updateOptions);
        await this.schedule.RunAsync(updateOperation);

        // Assert
        var state = updateOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal(initialModifiedTime, scheduleState.ScheduleLastModifiedAt);
    }

    [Fact]
    public async Task RunSchedule_WhenEndAtPassed_DoesNotRun()
    {
        // Arrange
        var endAt = DateTimeOffset.UtcNow.AddMinutes(-5); // End time in the past
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5))
        {
            EndAt = endAt
        };

        var createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        // Act
        var runOperation = new TestEntityOperation(
            nameof(Schedule.RunSchedule),
            createOperation.State,
            createOperation.State.GetState<ScheduleState>()?.ExecutionToken);
        await this.schedule.RunAsync(runOperation);

        // Assert
        var state = runOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Null(scheduleState.LastRunAt);
        Assert.Null(scheduleState.NextRunAt);
    }

    [Fact]
    public async Task RunSchedule_WithValidToken_UpdatesLastRunAt()
    {
        // Arrange
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5))
        {
            StartImmediatelyIfLate = true
        };

        var createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        var initialState = createOperation.State.GetState<ScheduleState>();
        var validToken = initialState?.ExecutionToken;

        // Act
        var runOperation = new TestEntityOperation(
            nameof(Schedule.RunSchedule),
            createOperation.State,
            validToken);
        await this.schedule.RunAsync(runOperation);

        // Assert
        var state = runOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.NotNull(scheduleState.LastRunAt);
    }

    [Fact]
    public async Task CreateSchedule_WithOrchestrationInput_SetsInputCorrectly()
    {
        // Arrange
        var orchestrationInput = "test-input";
        var options = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5))
        {
            OrchestrationInput = orchestrationInput
        };

        // Create test operation
        var operation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            options);

        // Act
        await this.schedule.RunAsync(operation);

        // Assert
        var state = operation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal(orchestrationInput, scheduleState.ScheduleConfiguration?.OrchestrationInput);
    }

    [Fact]
    public async Task CreateSchedule_WithOrchestrationInstanceId_SetsIdCorrectly()
    {
        // Arrange
        var instanceId = "custom-instance-id";
        var options = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5))
        {
            OrchestrationInstanceId = instanceId
        };

        // Create test operation
        var operation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            options);

        // Act
        await this.schedule.RunAsync(operation);

        // Assert
        var state = operation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal(instanceId, scheduleState.ScheduleConfiguration?.OrchestrationInstanceId);
    }

    [Fact]
    public async Task UpdateSchedule_WithOrchestrationInput_UpdatesInputCorrectly()
    {
        // Arrange
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        var createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        var newInput = "new-test-input";
        var updateOptions = new ScheduleUpdateOptions
        {
            OrchestrationInput = newInput
        };

        // Act
        var updateOperation = new TestEntityOperation(
            nameof(Schedule.UpdateSchedule),
            createOperation.State,
            updateOptions);
        await this.schedule.RunAsync(updateOperation);

        // Assert
        var state = updateOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal(newInput, scheduleState.ScheduleConfiguration?.OrchestrationInput);
    }

    [Fact]
    public async Task UpdateSchedule_WithStartImmediatelyIfLate_UpdatesPropertyCorrectly()
    {
        // Arrange
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        var createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        var updateOptions = new ScheduleUpdateOptions
        {
            StartImmediatelyIfLate = true
        };

        // Act
        var updateOperation = new TestEntityOperation(
            nameof(Schedule.UpdateSchedule),
            createOperation.State,
            updateOptions);
        await this.schedule.RunAsync(updateOperation);

        // Assert
        var state = updateOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.True(scheduleState.ScheduleConfiguration?.StartImmediatelyIfLate);
    }

    [Fact]
    public async Task RunSchedule_WhenStartAtInFuture_DoesNotRunImmediately()
    {
        // Arrange
        var startAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5))
        {
            StartAt = startAt
        };

        var createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        // Act
        var runOperation = new TestEntityOperation(
            nameof(Schedule.RunSchedule),
            createOperation.State,
            createOperation.State.GetState<ScheduleState>()?.ExecutionToken);
        await this.schedule.RunAsync(runOperation);

        // Assert
        var state = runOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Null(scheduleState.LastRunAt);
        Assert.NotNull(scheduleState.NextRunAt);
        Assert.True(scheduleState.NextRunAt >= startAt);
    }

    [Fact]
    public async Task RunSchedule_WithStartImmediatelyIfLate_RunsImmediatelyWhenLate()
    {
        // Arrange
        var startAt = DateTimeOffset.UtcNow.AddMinutes(-5); // Start time in the past
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5))
        {
            StartAt = startAt,
            StartImmediatelyIfLate = true
        };

        var createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        // Act
        var runOperation = new TestEntityOperation(
            nameof(Schedule.RunSchedule),
            createOperation.State,
            createOperation.State.GetState<ScheduleState>()?.ExecutionToken);
        await this.schedule.RunAsync(runOperation);

        // Assert
        var state = runOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.NotNull(scheduleState.LastRunAt);
        Assert.True(scheduleState.LastRunAt >= DateTimeOffset.UtcNow.AddSeconds(-1));
    }

    [Fact]
    public async Task RunSchedule_WithStartImmediatelyIfLate_False_DoesNotRunImmediatelyWhenLate()
    {
        // Arrange
        var startAt = DateTimeOffset.UtcNow.AddMinutes(-5); // Start time in the past
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5))
        {
            StartAt = startAt,
            StartImmediatelyIfLate = false
        };

        var createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        // Act
        var runOperation = new TestEntityOperation(
            nameof(Schedule.RunSchedule),
            createOperation.State,
            createOperation.State.GetState<ScheduleState>()?.ExecutionToken);
        await this.schedule.RunAsync(runOperation);

        // Assert
        var state = runOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Null(scheduleState.LastRunAt);
        Assert.NotNull(scheduleState.NextRunAt);
        Assert.True(scheduleState.NextRunAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task CreateSchedule_WithEndAtBeforeStartAt_ThrowsArgumentException()
    {
        // Arrange
        var startAt = DateTimeOffset.UtcNow.AddHours(2);
        var endAt = DateTimeOffset.UtcNow.AddHours(1); // Before startAt
        var options = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5))
        {
            StartAt = startAt,
            EndAt = endAt
        };

        // Create test operation
        var operation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            options);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            this.schedule.RunAsync(operation).AsTask());
    }

    [Fact]
    public async Task RunSchedule_WithExpiredEndAt_DoesNotUpdateLastRunAt()
    {
        // Arrange
        var endAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5))
        {
            EndAt = endAt
        };

        var createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        var initialState = createOperation.State.GetState<ScheduleState>();

        // Act
        var runOperation = new TestEntityOperation(
            nameof(Schedule.RunSchedule),
            createOperation.State,
            initialState?.ExecutionToken);
        await this.schedule.RunAsync(runOperation);

        // Assert
        var state = runOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Null(scheduleState.LastRunAt);
    }

    [Fact]
    public async Task CreateSchedule_WithMaxInterval_CreatesSchedule()
    {
        // Arrange
        var options = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.MaxValue);

        // Create test operation
        var operation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            options);

        // Act
        await this.schedule.RunAsync(operation);

        // Assert
        var state = operation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal(TimeSpan.MaxValue, scheduleState.ScheduleConfiguration?.Interval);
    }

    [Fact]
    public async Task UpdateSchedule_WithSameValues_DoesNotUpdateModifiedTime()
    {
        // Arrange
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        var createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        var initialState = createOperation.State.GetState<ScheduleState>();
        var initialModifiedTime = initialState?.ScheduleLastModifiedAt;

        var updateOptions = new ScheduleUpdateOptions
        {
            OrchestrationName = "TestOrchestration", // Same name
            Interval = TimeSpan.FromMinutes(5) // Same interval
        };

        // Act
        var updateOperation = new TestEntityOperation(
            nameof(Schedule.UpdateSchedule),
            createOperation.State,
            updateOptions);
        await this.schedule.RunAsync(updateOperation);

        // Assert
        var state = updateOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal(initialModifiedTime, scheduleState.ScheduleLastModifiedAt);
    }

    [Fact]
    public async Task RunSchedule_WithStartAtInFutureAndStartImmediatelyIfLate_DoesNotRunImmediately()
    {
        // Arrange
        var startAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5))
        {
            StartAt = startAt,
            StartImmediatelyIfLate = true // Should be ignored since StartAt is in future
        };

        var createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        // Act
        var runOperation = new TestEntityOperation(
            nameof(Schedule.RunSchedule),
            createOperation.State,
            createOperation.State.GetState<ScheduleState>()?.ExecutionToken);
        await this.schedule.RunAsync(runOperation);

        // Assert
        var state = runOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Null(scheduleState.LastRunAt);
        Assert.Equal(startAt, scheduleState.NextRunAt);
    }

    [Fact]
    public async Task CreateSchedule_WithMaxDateTimeOffset_CreatesSchedule()
    {
        // Arrange
        var options = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5))
        {
            EndAt = DateTimeOffset.MaxValue
        };

        // Create test operation
        var operation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            options);

        // Act
        await this.schedule.RunAsync(operation);

        // Assert
        var state = operation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal(DateTimeOffset.MaxValue, scheduleState.ScheduleConfiguration?.EndAt);
    }

    [Fact]
    public async Task CreateSchedule_WithNullEndAt_ClearsEndAt()
    {
        // Arrange
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5))
        {
            EndAt = DateTimeOffset.UtcNow.AddHours(1)
        };

        var createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        var createOptions2 = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5))
        {
            EndAt = null
        };

        // Act
        var updateOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            createOperation.State,
            createOptions2);
        await this.schedule.RunAsync(updateOperation);

        // Assert
        var state = updateOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Null(scheduleState.ScheduleConfiguration?.EndAt);
    }

    [Fact]
    public async Task RunSchedule_WithMultipleTokens_OnlyExecutesLatestToken()
    {
        // Arrange
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5))
        {
            StartImmediatelyIfLate = true
        };

        var createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        var initialState = createOperation.State.GetState<ScheduleState>();
        var initialToken = initialState?.ExecutionToken;

        // Update to get new token
        var updateOperation = new TestEntityOperation(
            nameof(Schedule.UpdateSchedule),
            createOperation.State,
            new ScheduleUpdateOptions { Interval = TimeSpan.FromMinutes(6) });
        await this.schedule.RunAsync(updateOperation);

        var updatedState = updateOperation.State.GetState<ScheduleState>();
        var newToken = updatedState?.ExecutionToken;

        // Try to run with old token
        var oldTokenOperation = new TestEntityOperation(
            nameof(Schedule.RunSchedule),
            updateOperation.State,
            initialToken);
        await this.schedule.RunAsync(oldTokenOperation);

        // Assert old token operation didn't execute
        var stateAfterOldToken = oldTokenOperation.State.GetState<ScheduleState>();
        Assert.Null(stateAfterOldToken?.LastRunAt);

        // Run with new token
        var newTokenOperation = new TestEntityOperation(
            nameof(Schedule.RunSchedule),
            oldTokenOperation.State,
            newToken);
        await this.schedule.RunAsync(newTokenOperation);

        // Assert new token operation executed
        var finalState = newTokenOperation.State.GetState<ScheduleState>();
        Assert.NotNull(finalState?.LastRunAt);
    }

    [Fact]
    public async Task CreateSchedule_WithLongOrchestrationName_CreatesSchedule()
    {
        // Arrange
        string longName = new string('a', 1000); // 1000 character name
        var options = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: longName,
            interval: TimeSpan.FromMinutes(5));

        // Create test operation
        var operation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            options);

        // Act
        await this.schedule.RunAsync(operation);

        // Assert
        var state = operation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal(longName, scheduleState.ScheduleConfiguration?.OrchestrationName);
    }

    [Fact]
    public async Task UpdateSchedule_WithLargeInput_UpdatesInput()
    {
        // Arrange
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        var createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        string largeInput = new string('x', 10000); // 10KB input
        var updateOptions = new ScheduleUpdateOptions
        {
            OrchestrationInput = largeInput
        };

        // Act
        var updateOperation = new TestEntityOperation(
            nameof(Schedule.UpdateSchedule),
            createOperation.State,
            updateOptions);
        await this.schedule.RunAsync(updateOperation);

        // Assert
        var state = updateOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal(largeInput, scheduleState.ScheduleConfiguration?.OrchestrationInput);
    }

    [Fact]
    public async Task RunSchedule_WithPreciseInterval_CalculatesNextRunTimeCorrectly()
    {
        // Arrange
        var startAt = DateTimeOffset.UtcNow;
        var interval = TimeSpan.FromSeconds(1.5); // 1.5 seconds
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: interval)
        {
            StartAt = startAt
        };

        var createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        // Act
        var runOperation = new TestEntityOperation(
            nameof(Schedule.RunSchedule),
            createOperation.State,
            createOperation.State.GetState<ScheduleState>()?.ExecutionToken);
        await this.schedule.RunAsync(runOperation);

        // Assert
        var state = runOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.NotNull(scheduleState.NextRunAt);
        var expectedNextRun = startAt.Add(interval);
        Assert.Equal(expectedNextRun.Ticks, scheduleState.NextRunAt.Value.Ticks);
    }

    [Fact]
    public async Task CreateSchedule_WithMinDateTimeOffset_CreatesSchedule()
    {
        // Arrange
        var options = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5))
        {
            StartAt = DateTimeOffset.MinValue
        };

        // Create test operation
        var operation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            options);

        // Act
        await this.schedule.RunAsync(operation);

        // Assert
        var state = operation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal(DateTimeOffset.MinValue, scheduleState.ScheduleConfiguration?.StartAt);
    }

    [Fact]
    public async Task UpdateSchedule_WithAllFieldsNull_DoesNotModifyState()
    {
        // Arrange
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        var createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        var initialState = createOperation.State.GetState<ScheduleState>();
        var updateOptions = new ScheduleUpdateOptions();

        // Act
        var updateOperation = new TestEntityOperation(
            nameof(Schedule.UpdateSchedule),
            createOperation.State,
            updateOptions);
        await this.schedule.RunAsync(updateOperation);

        // Assert
        var state = updateOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal(initialState?.ScheduleConfiguration?.OrchestrationName, scheduleState.ScheduleConfiguration?.OrchestrationName);
        Assert.Equal(initialState?.ScheduleConfiguration?.Interval, scheduleState.ScheduleConfiguration?.Interval);
        Assert.Equal(initialState?.ScheduleConfiguration?.StartAt, scheduleState.ScheduleConfiguration?.StartAt);
        Assert.Equal(initialState?.ScheduleConfiguration?.EndAt, scheduleState.ScheduleConfiguration?.EndAt);
        Assert.Equal(initialState?.ScheduleConfiguration?.StartImmediatelyIfLate, scheduleState.ScheduleConfiguration?.StartImmediatelyIfLate);
    }

    [Fact]
    public async Task RunSchedule_WithIntervalSmallerThanTimeSinceStart_CalculatesCorrectNextRunTime()
    {
        // Arrange
        var startAt = DateTimeOffset.UtcNow.AddMinutes(-10); // 10 minutes ago
        var interval = TimeSpan.FromMinutes(3); // 3 minute interval
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: interval)
        {
            StartAt = startAt
        };

        var createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        // Act
        var runOperation = new TestEntityOperation(
            nameof(Schedule.RunSchedule),
            createOperation.State,
            createOperation.State.GetState<ScheduleState>()?.ExecutionToken);
        await this.schedule.RunAsync(runOperation);

        // Assert
        var state = runOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.NotNull(scheduleState.NextRunAt);

        // Calculate expected next run time
        // Number of intervals elapsed = 10 minutes / 3 minutes = 3 intervals (rounded down)
        // Next run should be at start time + (intervals elapsed + 1) * interval
        var expectedNextRun = startAt.AddTicks((3 + 1) * interval.Ticks);
        Assert.Equal(expectedNextRun, scheduleState.NextRunAt.Value);
    }

    [Fact]
    public async Task UpdateSchedule_WithEmptyOrchestrationName_NothingChange()
    {
        // Arrange
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        var createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        var updateOptions = new ScheduleUpdateOptions
        {
            OrchestrationName = ""
        };

        // Act & Assert
        var updateOperation = new TestEntityOperation(
            nameof(Schedule.UpdateSchedule),
            createOperation.State,
            updateOptions);
        await this.schedule.RunAsync(updateOperation);

        // assert nothing changed
        var state = updateOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal("TestOrchestration", scheduleState.ScheduleConfiguration?.OrchestrationName);
        Assert.Equal(TimeSpan.FromMinutes(5), scheduleState.ScheduleConfiguration?.Interval);
        Assert.Equal(createOperation.State.GetState<ScheduleState>()?.ScheduleConfiguration?.StartAt, scheduleState.ScheduleConfiguration?.StartAt);
        Assert.Null(scheduleState.ScheduleConfiguration?.EndAt);
        Assert.False(scheduleState.ScheduleConfiguration?.StartImmediatelyIfLate);
        Assert.Equal(createOperation.State.GetState<ScheduleState>()?.ExecutionToken, scheduleState.ExecutionToken);
        Assert.Equal(ScheduleStatus.Active, scheduleState.Status);
        Assert.Equal(createOperation.State.GetState<ScheduleState>()?.LastRunAt, scheduleState.LastRunAt);
        Assert.Equal(createOperation.State.GetState<ScheduleState>()?.NextRunAt, scheduleState.NextRunAt);
        Assert.Equal(createOperation.State.GetState<ScheduleState>()?.ScheduleConfiguration?.OrchestrationInput, scheduleState.ScheduleConfiguration?.OrchestrationInput);
        Assert.Equal(createOperation.State.GetState<ScheduleState>()?.ScheduleConfiguration?.OrchestrationInstanceId, scheduleState.ScheduleConfiguration?.OrchestrationInstanceId);
        Assert.Equal(createOperation.State.GetState<ScheduleState>()?.ScheduleConfiguration?.OrchestrationName, scheduleState.ScheduleConfiguration?.OrchestrationName);
    }

    [Fact]
    public async Task RunSchedule_WithEmptyToken_DoesNotRun()
    {
        // Arrange
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        var createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        // Act
        var runOperation = new TestEntityOperation(
            nameof(Schedule.RunSchedule),
            createOperation.State,
            "");
        await this.schedule.RunAsync(runOperation);

        // Assert
        var state = runOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Null(scheduleState.LastRunAt);

        Assert.Contains($"Schedule '{this.scheduleId}' run cancelled with execution token ''", this.logger.Logs.Last().Message);
    }

    [Fact]
    public async Task CreateSchedule_WithSpecialCharactersInScheduleId_CreatesSchedule()
    {
        // Arrange
        var options = new ScheduleCreationOptions(
            scheduleId: "test@schedule#123$%^",
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        // Create test operation
        var operation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            options);

        // Act
        await this.schedule.RunAsync(operation);

        // Assert
        var state = operation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal("test@schedule#123$%^", scheduleState.ScheduleConfiguration?.ScheduleId);
    }

    [Fact]
    public async Task CreateSchedule_WithUnicodeCharactersInScheduleId_CreatesSchedule()
    {
        // Arrange
        var options = new ScheduleCreationOptions(
            scheduleId: "测试时间表",
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        // Create test operation
        var operation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            options);

        // Act
        await this.schedule.RunAsync(operation);

        // Assert
        var state = operation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal("测试时间表", scheduleState.ScheduleConfiguration?.ScheduleId);
    }

    [Fact]
    public async Task UpdateSchedule_WithVeryLongOrchestrationInstanceId_UpdatesSuccessfully()
    {
        // Arrange
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        var createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        string longInstanceId = new string('x', 1000);
        var updateOptions = new ScheduleUpdateOptions
        {
            OrchestrationInstanceId = longInstanceId
        };

        // Act
        var updateOperation = new TestEntityOperation(
            nameof(Schedule.UpdateSchedule),
            createOperation.State,
            updateOptions);
        await this.schedule.RunAsync(updateOperation);

        // Assert
        var state = updateOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal(longInstanceId, scheduleState.ScheduleConfiguration?.OrchestrationInstanceId);
    }

    [Fact]
    public async Task RunSchedule_WithWhitespaceToken_DoesNotRun()
    {
        // Arrange
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        var createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        // Act
        var runOperation = new TestEntityOperation(
            nameof(Schedule.RunSchedule),
            createOperation.State,
            "   ");
        await this.schedule.RunAsync(runOperation);

        // Assert
        var state = runOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Null(scheduleState.LastRunAt);
        Assert.Contains($"Schedule '{this.scheduleId}' run cancelled with execution token '   '", this.logger.Logs.Last().Message);
    }

    [Fact]
    public async Task CreateSchedule_WithMaxLengthScheduleId_CreatesSchedule()
    {
        // Arrange
        string maxLengthId = new string('x', 1000);
        var options = new ScheduleCreationOptions(
            scheduleId: maxLengthId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        // Create test operation
        var operation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            options);

        // Act
        await this.schedule.RunAsync(operation);

        // Assert
        var state = operation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal(maxLengthId, scheduleState.ScheduleConfiguration?.ScheduleId);
    }

    [Fact]
    public async Task CreateSchedule_WithJsonSpecialCharactersInInput_CreatesSchedule()
    {
        // Arrange
        string specialInput = "{\"key\":\"value\",\"array\":[1,2,3],\"nested\":{\"prop\":true}}";
        var options = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5))
        {
            OrchestrationInput = specialInput
        };

        // Create test operation
        var operation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            options);

        // Act
        await this.schedule.RunAsync(operation);

        // Assert
        var state = operation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal(specialInput, scheduleState.ScheduleConfiguration?.OrchestrationInput);
    }

    [Fact]
    public async Task UpdateSchedule_WithJsonSpecialCharactersInInput_UpdatesSuccessfully()
    {
        // Arrange
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        var createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        string specialInput = "{\"key\":\"value\",\"array\":[1,2,3],\"nested\":{\"prop\":true}}";
        var updateOptions = new ScheduleUpdateOptions
        {
            OrchestrationInput = specialInput
        };

        // Act
        var updateOperation = new TestEntityOperation(
            nameof(Schedule.UpdateSchedule),
            createOperation.State,
            updateOptions);
        await this.schedule.RunAsync(updateOperation);

        // Assert
        var state = updateOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal(specialInput, scheduleState.ScheduleConfiguration?.OrchestrationInput);
    }

    [Fact]
    public async Task CreateSchedule_WithHtmlSpecialCharactersInInput_CreatesSchedule()
    {
        // Arrange
        string htmlInput = "<div class=\"test\">Hello & World</div>";
        var options = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5))
        {
            OrchestrationInput = htmlInput
        };

        // Create test operation
        var operation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            options);

        // Act
        await this.schedule.RunAsync(operation);

        // Assert
        var state = operation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal(htmlInput, scheduleState.ScheduleConfiguration?.OrchestrationInput);
    }

    [Fact]
    public async Task UpdateSchedule_WithHtmlSpecialCharactersInInput_UpdatesSuccessfully()
    {
        // Arrange
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        var createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        string htmlInput = "<div class=\"test\">Hello & World</div>";
        var updateOptions = new ScheduleUpdateOptions
        {
            OrchestrationInput = htmlInput
        };

        // Act
        var updateOperation = new TestEntityOperation(
            nameof(Schedule.UpdateSchedule),
            createOperation.State,
            updateOptions);
        await this.schedule.RunAsync(updateOperation);

        // Assert
        var state = updateOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal(htmlInput, scheduleState.ScheduleConfiguration?.OrchestrationInput);
    }

    [Fact]
    public async Task CreateSchedule_WithMultilineInput_CreatesSchedule()
    {
        // Arrange
        string multilineInput = @"Line 1
Line 2
Line 3
With special chars: !@#$%^&*()";
        var options = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5))
        {
            OrchestrationInput = multilineInput
        };

        // Create test operation
        var operation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            options);

        // Act
        await this.schedule.RunAsync(operation);

        // Assert
        var state = operation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal(multilineInput, scheduleState.ScheduleConfiguration?.OrchestrationInput);
    }

    [Fact]
    public async Task UpdateSchedule_WithMultilineInput_UpdatesSuccessfully()
    {
        // Arrange
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        var createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        string multilineInput = @"Line 1
Line 2
Line 3
With special chars: !@#$%^&*()";
        var updateOptions = new ScheduleUpdateOptions
        {
            OrchestrationInput = multilineInput
        };

        // Act
        var updateOperation = new TestEntityOperation(
            nameof(Schedule.UpdateSchedule),
            createOperation.State,
            updateOptions);
        await this.schedule.RunAsync(updateOperation);

        // Assert
        var state = updateOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal(multilineInput, scheduleState.ScheduleConfiguration?.OrchestrationInput);
    }

    [Fact]
    public async Task CreateSchedule_WithBase64EncodedInput_CreatesSchedule()
    {
        // Arrange
        string base64Input = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("Hello World"));
        var options = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5))
        {
            OrchestrationInput = base64Input
        };

        // Create test operation
        var operation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            options);

        // Act
        await this.schedule.RunAsync(operation);

        // Assert
        var state = operation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal(base64Input, scheduleState.ScheduleConfiguration?.OrchestrationInput);
    }

    [Fact]
    public async Task UpdateSchedule_WithBase64EncodedInput_UpdatesSuccessfully()
    {
        // Arrange
        var createOptions = new ScheduleCreationOptions(
            scheduleId: this.scheduleId,
            orchestrationName: "TestOrchestration",
            interval: TimeSpan.FromMinutes(5));

        var createOperation = new TestEntityOperation(
            nameof(Schedule.CreateSchedule),
            new TestEntityState(null),
            createOptions);
        await this.schedule.RunAsync(createOperation);

        string base64Input = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("Hello World"));
        var updateOptions = new ScheduleUpdateOptions
        {
            OrchestrationInput = base64Input
        };

        // Act
        var updateOperation = new TestEntityOperation(
            nameof(Schedule.UpdateSchedule),
            createOperation.State,
            updateOptions);
        await this.schedule.RunAsync(updateOperation);

        // Assert
        var state = updateOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Equal(base64Input, scheduleState.ScheduleConfiguration?.OrchestrationInput);
    }
}