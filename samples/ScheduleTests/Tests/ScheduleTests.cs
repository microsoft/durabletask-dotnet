// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.ScheduledTasks;
using ScheduleTests.Infrastructure;
using ScheduleTests.Tasks;
using Xunit;

namespace ScheduleTests.Tests
{
    public class ScheduleTests : ScheduleTestBase
    {
        [Fact]
        public async Task SimpleSchedule_ShouldExecuteAfterDelay()
        {
            // Arrange
            var scheduleId = $"simple-schedule-{Guid.NewGuid()}";
            var startTime = DateTimeOffset.UtcNow.AddSeconds(30);
            var creationOptions = new ScheduleCreationOptions(
                scheduleId,
                nameof(SimpleOrchestrator),
                TimeSpan.FromMinutes(1))
            {
                StartAt = startTime,
                OrchestrationInput = "test1",
                StartImmediatelyIfLate = false
            };

            // Act
            var scheduleClient = await this.ScheduledTaskClient.CreateScheduleAsync(creationOptions);

            // Wait for a bit to ensure schedule executes
            await Task.Delay(TimeSpan.FromMinutes(2));

            // Assert
            var description = await scheduleClient.DescribeAsync();
            Assert.NotNull(description);
            Assert.Equal(ScheduleStatus.Active, description.Status);
            Assert.Equal(scheduleId, description.ScheduleId);
        }

        [Fact]
        public async Task RecurringSchedule_ShouldExecuteMultipleTimes()
        {
            // Arrange
            var scheduleId = $"recurring-schedule-{Guid.NewGuid()}";
            var startTime = DateTimeOffset.UtcNow;
            var creationOptions = new ScheduleCreationOptions(
                scheduleId,
                nameof(LongRunningOrchestrator),
                TimeSpan.FromSeconds(30))
            {
                StartAt = startTime,
                OrchestrationInput = "test2",
                StartImmediatelyIfLate = true
            };

            // Act
            var scheduleClient = await this.ScheduledTaskClient.CreateScheduleAsync(creationOptions);

            // Wait for multiple executions
            await Task.Delay(TimeSpan.FromMinutes(2));

            // Assert
            var description = await scheduleClient.DescribeAsync();
            Assert.NotNull(description);
            Assert.Equal(ScheduleStatus.Active, description.Status);

            // Cleanup
            await scheduleClient.DeleteAsync();
        }

        [Fact]
        public async Task CronSchedule_ShouldExecuteOnSchedule()
        {
            // Arrange
            var scheduleId = $"cron-schedule-{Guid.NewGuid()}";
            var startTime = DateTimeOffset.UtcNow;
            var creationOptions = new ScheduleCreationOptions(
                scheduleId,
                nameof(RandomRunTimeOrchestrator),
                TimeSpan.FromSeconds(45))
            {
                StartAt = startTime,
                OrchestrationInput = "test3",
                StartImmediatelyIfLate = true,
                EndAt = startTime.AddMinutes(3)
            };

            // Act
            var scheduleClient = await this.ScheduledTaskClient.CreateScheduleAsync(creationOptions);

            // Wait for a couple of executions
            await Task.Delay(TimeSpan.FromMinutes(2));

            // Assert
            var description = await scheduleClient.DescribeAsync();
            Assert.NotNull(description);
            Assert.Equal(ScheduleStatus.Active, description.Status);

            // Test pause functionality
            await scheduleClient.PauseAsync();
            description = await scheduleClient.DescribeAsync();
            Assert.Equal(ScheduleStatus.Paused, description.Status);

            // Test resume functionality
            await scheduleClient.ResumeAsync();
            description = await scheduleClient.DescribeAsync();
            Assert.Equal(ScheduleStatus.Active, description.Status);

            // Cleanup
            await scheduleClient.DeleteAsync();
        }

        [Fact]
        public async Task Schedule_ShouldHandleUpdateOperation()
        {
            // Arrange
            var scheduleId = $"update-schedule-{Guid.NewGuid()}";
            var startTime = DateTimeOffset.UtcNow;
            var creationOptions = new ScheduleCreationOptions(
                scheduleId,
                nameof(SimpleOrchestrator),
                TimeSpan.FromMinutes(1))
            {
                StartAt = startTime,
                OrchestrationInput = "initial"
            };

            // Act
            var scheduleClient = await this.ScheduledTaskClient.CreateScheduleAsync(creationOptions);

            // Update the schedule
            var updateOptions = new ScheduleUpdateOptions
            {
                OrchestrationInput = "updated",
                Interval = TimeSpan.FromMinutes(2)
            };
            await scheduleClient.UpdateAsync(updateOptions);

            // Assert
            var description = await scheduleClient.DescribeAsync();
            Assert.NotNull(description);
            Assert.Equal("updated", description.OrchestrationInput);
            Assert.Equal(TimeSpan.FromMinutes(2), description.Interval);

            // Cleanup
            await scheduleClient.DeleteAsync();
        }
    }
}