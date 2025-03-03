// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit;

namespace Microsoft.DurableTask.ScheduledTasks.Tests.Models;

public class ScheduleStateTests
{
    [Fact]
    public void DefaultConstructor_InitializesCorrectly()
    {
        // Act
        var state = new ScheduleState();

        // Assert
        Assert.Equal(ScheduleStatus.Uninitialized, state.Status);
        Assert.NotNull(state.ExecutionToken);
        Assert.NotEmpty(state.ExecutionToken);
        Assert.Null(state.LastRunAt);
        Assert.Null(state.NextRunAt);
        Assert.Null(state.ScheduleCreatedAt);
        Assert.Null(state.ScheduleLastModifiedAt);
        Assert.Null(state.ScheduleConfiguration);
    }

    [Fact]
    public void RefreshScheduleRunExecutionToken_GeneratesNewToken()
    {
        // Arrange
        var state = new ScheduleState();
        string originalToken = state.ExecutionToken;

        // Act
        state.RefreshScheduleRunExecutionToken();

        // Assert
        Assert.NotEqual(originalToken, state.ExecutionToken);
        Assert.NotEmpty(state.ExecutionToken);
    }

    [Fact]
    public void RefreshScheduleRunExecutionToken_GeneratesUniqueTokens()
    {
        // Arrange
        var state = new ScheduleState();
        var tokens = new HashSet<string>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            state.RefreshScheduleRunExecutionToken();
            tokens.Add(state.ExecutionToken);
        }

        // Assert
        Assert.Equal(100, tokens.Count); // All tokens should be unique
    }

    [Fact]
    public void Properties_SetAndGetCorrectly()
    {
        // Arrange
        var state = new ScheduleState();
        var now = DateTimeOffset.UtcNow;
        var config = new ScheduleConfiguration("test-id", "test-orchestration", TimeSpan.FromMinutes(5));

        // Act
        state.Status = ScheduleStatus.Active;
        state.LastRunAt = now;
        state.NextRunAt = now.AddMinutes(5);
        state.ScheduleCreatedAt = now;
        state.ScheduleLastModifiedAt = now;
        state.ScheduleConfiguration = config;

        // Assert
        Assert.Equal(ScheduleStatus.Active, state.Status);
        Assert.Equal(now, state.LastRunAt);
        Assert.Equal(now.AddMinutes(5), state.NextRunAt);
        Assert.Equal(now, state.ScheduleCreatedAt);
        Assert.Equal(now, state.ScheduleLastModifiedAt);
        Assert.Same(config, state.ScheduleConfiguration);
    }
} 