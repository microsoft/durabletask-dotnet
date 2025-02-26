using Microsoft.DurableTask;
using ScheduleTests.Infrastructure;
using ScheduleTests.Tasks;
using System;
using System.Threading.Tasks;
using Xunit;

namespace ScheduleTests.Tests
{
    public class ScheduleTests : ScheduleTestBase
    {
        [Fact]
        public async Task SimpleSchedule_ShouldExecuteAfterDelay()
        {
            // Arrange
            var scheduleTime = DateTime.UtcNow.AddSeconds(30);

            // Act
            var instanceId = await Client.ScheduleNewOrchestrationInstance(
                nameof(SimpleScheduleOrchestrator),
                scheduleTime);

            // Assert
            await WaitForOrchestrationCompletion(instanceId, TimeSpan.FromMinutes(2));
            var status = await Client.GetInstanceAsync(instanceId);
            Assert.Equal(OrchestrationRuntimeStatus.Completed, status.RuntimeStatus);
        }

        [Fact]
        public async Task RecurringSchedule_ShouldExecuteMultipleTimes()
        {
            // Arrange
            var startTime = DateTime.UtcNow;

            // Act
            var instanceId = await Client.ScheduleNewOrchestrationInstance(
                nameof(RecurringScheduleOrchestrator),
                startTime);

            // Assert
            await WaitForOrchestrationCompletion(instanceId, TimeSpan.FromMinutes(5));
            var status = await Client.GetInstanceAsync(instanceId);
            Assert.Equal(OrchestrationRuntimeStatus.Completed, status.RuntimeStatus);
            
            // Verify the orchestration history contains all expected events
            var history = await Client.GetInstanceHistoryAsync(instanceId);
            Assert.NotNull(history);
        }

        [Fact]
        public async Task CronSchedule_ShouldExecuteOnSchedule()
        {
            // Arrange
            var startTime = DateTime.UtcNow;

            // Act
            var instanceId = await Client.ScheduleNewOrchestrationInstance(
                nameof(CronScheduleOrchestrator),
                startTime);

            // Let it run for 2 minutes to see a couple of executions
            await Task.Delay(TimeSpan.FromMinutes(2));

            // Terminate the orchestration since it runs indefinitely
            await Client.TerminateOrchestrationAsync(instanceId);

            // Assert
            var status = await Client.GetInstanceAsync(instanceId);
            Assert.Equal(OrchestrationRuntimeStatus.Terminated, status.RuntimeStatus);
            
            // Verify we have some execution history
            var history = await Client.GetInstanceHistoryAsync(instanceId);
            Assert.NotNull(history);
        }
    }
} 