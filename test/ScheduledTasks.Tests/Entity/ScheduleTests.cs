// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities.Tests;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.DurableTask.ScheduledTasks.Tests.Entity;

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
            createOperation.State.GetState<ScheduleState>().ExecutionToken);
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
            createOperation.State.GetState<ScheduleState>().ExecutionToken);
        await this.schedule.RunAsync(runOperation);

        // Assert
        var state = runOperation.State.GetState(typeof(ScheduleState));
        Assert.NotNull(state);
        var scheduleState = Assert.IsType<ScheduleState>(state);
        Assert.Null(scheduleState.LastRunAt);
        Assert.NotNull(scheduleState.NextRunAt);
        Assert.True(scheduleState.NextRunAt > DateTimeOffset.UtcNow);
    }
}