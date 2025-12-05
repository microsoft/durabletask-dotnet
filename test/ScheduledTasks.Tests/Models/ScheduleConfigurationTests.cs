// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit;

namespace Microsoft.DurableTask.ScheduledTasks.Tests.Models;

public class ScheduleConfigurationTests
{
    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange
        string scheduleId = "test-schedule";
        string orchestrationName = "test-orchestration";
        TimeSpan interval = TimeSpan.FromMinutes(5);

        // Act
        var config = new ScheduleConfiguration(scheduleId, orchestrationName, interval);

        // Assert
        Assert.Equal(scheduleId, config.ScheduleId);
        Assert.Equal(orchestrationName, config.OrchestrationName);
        Assert.Equal(interval, config.Interval);
    }

    [Theory]
    [InlineData(null, "orchestration", typeof(ArgumentNullException), "Value cannot be null")]
    [InlineData("", "orchestration", typeof(ArgumentException), "Parameter cannot be an empty string or start with the null character")]
    [InlineData("schedule", null, typeof(ArgumentNullException), "Value cannot be null")]
    [InlineData("schedule", "", typeof(ArgumentException), "Parameter cannot be an empty string or start with the null character")]
    public void Constructor_WithInvalidParameters_ThrowsException(string? scheduleId, string? orchestrationName, Type expectedExceptionType, string expectedMessage)
    {
        // Arrange
        TimeSpan interval = TimeSpan.FromMinutes(5);

        // Act & Assert
        var ex = Assert.Throws(expectedExceptionType, () => new ScheduleConfiguration(scheduleId!, orchestrationName!, interval));
        Assert.Contains(expectedMessage, ex.Message);
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
        var ex = Assert.Throws<ArgumentException>(() => new ScheduleConfiguration(scheduleId, orchestrationName, interval));
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
        var ex = Assert.Throws<ArgumentException>(() => new ScheduleConfiguration(scheduleId, orchestrationName, interval));
        Assert.Contains("Interval must be at least 1 second", ex.Message);
    }

    [Fact]
    public void OrchestrationName_SetToNull_ThrowsArgumentException()
    {
        // Arrange
        var config = new ScheduleConfiguration("test-schedule", "test-orchestration", TimeSpan.FromMinutes(5));

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => config.OrchestrationName = null!);
        Assert.Contains("Value cannot be null.", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Interval_SetToInvalidValue_ThrowsArgumentException()
    {
        // Arrange
        var config = new ScheduleConfiguration("test-schedule", "test-orchestration", TimeSpan.FromMinutes(5));

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => config.Interval = TimeSpan.Zero);
        Assert.Contains("Interval must be positive", ex.Message);

        ex = Assert.Throws<ArgumentException>(() => config.Interval = TimeSpan.FromMilliseconds(500));
        Assert.Contains("Interval must be at least 1 second", ex.Message);
    }

    [Fact]
    public void FromCreateOptions_WithValidOptions_CreatesConfiguration()
    {
        // Arrange
        var options = new ScheduleCreationOptions("test-schedule", "test-orchestration", TimeSpan.FromMinutes(5))
        {
            OrchestrationInput = "test-input",
            OrchestrationInstanceId = "test-instance",
            StartAt = DateTimeOffset.UtcNow,
            EndAt = DateTimeOffset.UtcNow.AddDays(1),
            StartImmediatelyIfLate = true
        };

        // Act
        var config = ScheduleConfiguration.FromCreateOptions(options);

        // Assert
        Assert.Equal(options.ScheduleId, config.ScheduleId);
        Assert.Equal(options.OrchestrationName, config.OrchestrationName);
        Assert.Equal(options.Interval, config.Interval);
        Assert.Equal(options.OrchestrationInput, config.OrchestrationInput);
        Assert.Equal(options.OrchestrationInstanceId, config.OrchestrationInstanceId);
        Assert.Equal(options.StartAt, config.StartAt);
        Assert.Equal(options.EndAt, config.EndAt);
        Assert.Equal(options.StartImmediatelyIfLate, config.StartImmediatelyIfLate);
    }

    [Fact]
    public void FromCreateOptions_WithNullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => ScheduleConfiguration.FromCreateOptions(null!));
        Assert.Equal("createOptions", ex.ParamName);
    }

    [Fact]
    public void Update_WithValidOptions_UpdatesConfiguration()
    {
        // Arrange
        var config = new ScheduleConfiguration("test-schedule", "test-orchestration", TimeSpan.FromMinutes(5));
        var updateOptions = new ScheduleUpdateOptions
        {
            OrchestrationName = "new-orchestration",
            OrchestrationInput = "new-input",
            OrchestrationInstanceId = "new-instance",
            StartAt = DateTimeOffset.UtcNow,
            EndAt = DateTimeOffset.UtcNow.AddDays(1),
            Interval = TimeSpan.FromMinutes(10),
            StartImmediatelyIfLate = true
        };

        // Act
        var updatedFields = config.Update(updateOptions);

        // Assert
        Assert.Equal(updateOptions.OrchestrationName, config.OrchestrationName);
        Assert.Equal(updateOptions.OrchestrationInput, config.OrchestrationInput);
        Assert.Equal(updateOptions.OrchestrationInstanceId, config.OrchestrationInstanceId);
        Assert.Equal(updateOptions.StartAt, config.StartAt);
        Assert.Equal(updateOptions.EndAt, config.EndAt);
        Assert.Equal(updateOptions.Interval, config.Interval);
        Assert.Equal(updateOptions.StartImmediatelyIfLate, config.StartImmediatelyIfLate);

        Assert.Contains(nameof(ScheduleConfiguration.OrchestrationName), updatedFields);
        Assert.Contains(nameof(ScheduleConfiguration.OrchestrationInput), updatedFields);
        Assert.Contains(nameof(ScheduleConfiguration.OrchestrationInstanceId), updatedFields);
        Assert.Contains(nameof(ScheduleConfiguration.StartAt), updatedFields);
        Assert.Contains(nameof(ScheduleConfiguration.EndAt), updatedFields);
        Assert.Contains(nameof(ScheduleConfiguration.Interval), updatedFields);
        Assert.Contains(nameof(ScheduleConfiguration.StartImmediatelyIfLate), updatedFields);
    }

    [Fact]
    public void Update_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        var config = new ScheduleConfiguration("test-schedule", "test-orchestration", TimeSpan.FromMinutes(5));

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => config.Update(null!));
        Assert.Equal("updateOptions", ex.ParamName);
    }
}