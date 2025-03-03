// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit;

namespace Microsoft.DurableTask.ScheduledTasks.Tests.Models;

public class ScheduleCreationOptionsTests
{
    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange
        string scheduleId = "test-schedule";
        string orchestrationName = "test-orchestration";
        TimeSpan interval = TimeSpan.FromMinutes(5);

        // Act
        var options = new ScheduleCreationOptions(scheduleId, orchestrationName, interval);

        // Assert
        Assert.Equal(scheduleId, options.ScheduleId);
        Assert.Equal(orchestrationName, options.OrchestrationName);
        Assert.Equal(interval, options.Interval);
        Assert.Null(options.OrchestrationInput);
        Assert.Null(options.OrchestrationInstanceId);
        Assert.Null(options.StartAt);
        Assert.Null(options.EndAt);
        Assert.False(options.StartImmediatelyIfLate);
    }

    [Theory]
    [InlineData(null, "orchestration", typeof(ArgumentNullException), "Value cannot be null")]
    [InlineData("", "orchestration", typeof(ArgumentException), "Parameter cannot be an empty string or start with the null character")]
    [InlineData("schedule", null, typeof(ArgumentNullException), "Value cannot be null.")]
    [InlineData("schedule", "", typeof(ArgumentException), "Parameter cannot be an empty string or start with the null character")]
    public void Constructor_WithInvalidParameters_ThrowsArgumentException(
        string scheduleId,
        string orchestrationName,
        Type expectedExceptionType,
        string expectedMessage)
    {
        // Arrange
        TimeSpan interval = TimeSpan.FromMinutes(5);

        // Act & Assert
        var exception = Assert.Throws(expectedExceptionType, () => new ScheduleCreationOptions(scheduleId, orchestrationName, interval));
        Assert.Contains(expectedMessage, exception.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void Constructor_WithNonPositiveInterval_ThrowsArgumentException(int seconds)
    {
        // Arrange
        string scheduleId = "test-schedule";
        string orchestrationName = "test-orchestration";
        TimeSpan interval = TimeSpan.FromSeconds(seconds);

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => new ScheduleCreationOptions(scheduleId, orchestrationName, interval));
        Assert.Contains("Interval must be positive", ex.Message);
    }

    [Fact]
    public void Constructor_WithIntervalLessThanOneSecond_ThrowsArgumentException()
    {
        // Arrange
        string scheduleId = "test-schedule";
        string orchestrationName = "test-orchestration";
        TimeSpan interval = TimeSpan.FromMilliseconds(500);

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => new ScheduleCreationOptions(scheduleId, orchestrationName, interval));
        Assert.Contains("Interval must be at least 1 second", ex.Message);
    }

    [Fact]
    public void Properties_SetAndGetCorrectly()
    {
        // Arrange
        var options = new ScheduleCreationOptions("test-schedule", "test-orchestration", TimeSpan.FromMinutes(5));
        var now = DateTimeOffset.UtcNow;

        // Act
        options = options with
        {
            OrchestrationInput = "test-input",
            OrchestrationInstanceId = "test-instance",
            StartAt = now,
            EndAt = now.AddDays(1),
            StartImmediatelyIfLate = true
        };

        // Assert
        Assert.Equal("test-input", options.OrchestrationInput);
        Assert.Equal("test-instance", options.OrchestrationInstanceId);
        Assert.Equal(now, options.StartAt);
        Assert.Equal(now.AddDays(1), options.EndAt);
        Assert.True(options.StartImmediatelyIfLate);
    }

    [Fact]
    public void WithOperator_CreatesNewInstance()
    {
        // Arrange
        var original = new ScheduleCreationOptions("test-schedule", "test-orchestration", TimeSpan.FromMinutes(5));
        var now = DateTimeOffset.UtcNow;

        // Act
        var modified = original with
        {
            OrchestrationInput = "test-input",
            StartAt = now
        };

        // Assert
        Assert.Equal(original.ScheduleId, modified.ScheduleId);
        Assert.Equal(original.OrchestrationName, modified.OrchestrationName);
        Assert.Equal(original.Interval, modified.Interval);
        Assert.Equal("test-input", modified.OrchestrationInput);
        Assert.Equal(now, modified.StartAt);
        Assert.NotSame(original, modified);
    }
}