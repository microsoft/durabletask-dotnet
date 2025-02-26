// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.ScheduledTasks;
using ScheduleTests.Infrastructure;
using ScheduleTests.Tasks;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace ScheduleTests.Tests
{
    public class ScheduleTests : ScheduleTestBase
    {
        [Fact]
        public async Task SimpleSchedule_ShouldExecuteOnce()
        {
            var scheduleId = $"simple-once-{Guid.NewGuid()}";
            try
            {
                var startTime = DateTimeOffset.UtcNow.AddSeconds(5);
                var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
                    scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromMinutes(5))
                {
                    StartAt = startTime,
                    EndAt = startTime.AddMinutes(1),
                    OrchestrationInput = "once",
                    StartImmediatelyIfLate = false
                });

                await Task.Delay(TimeSpan.FromSeconds(10));
                var desc = await client.DescribeAsync();
                Assert.Equal(ScheduleStatus.Active, desc.Status);
            }
            finally
            {
                await CleanupSchedule(scheduleId);
            }
        }

        [Fact]
        public async Task Schedule_ShouldRespectStartTime()
        {
            var scheduleId = $"start-time-{Guid.NewGuid()}";
            try
            {
                var startTime = DateTimeOffset.UtcNow.AddSeconds(30);
                var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
                    scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromMinutes(1))
                {
                    StartAt = startTime,
                    OrchestrationInput = "delayed-start"
                });

                // Check immediately - should not be started
                var desc = await client.DescribeAsync();
                Assert.Equal(startTime, desc.NextRunAt);
            }
            finally
            {
                await CleanupSchedule(scheduleId);
            }
        }

        [Fact]
        public async Task Schedule_ShouldRespectEndTime()
        {
            var scheduleId = $"end-time-{Guid.NewGuid()}";
            try
            {
                var startTime = DateTimeOffset.UtcNow;
                var endTime = startTime.AddSeconds(10);
                var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
                    scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromSeconds(5))
                {
                    StartAt = startTime,
                    EndAt = endTime,
                    OrchestrationInput = "with-end"
                });

                await Task.Delay(TimeSpan.FromSeconds(15));
                var desc = await client.DescribeAsync();
                Assert.Equal(ScheduleStatus.Uninitialized, desc.Status);
            }
            finally
            {
                await CleanupSchedule(scheduleId);
            }
        }

        [Fact]
        public async Task Schedule_ShouldHandleImmediateStart()
        {
            var scheduleId = $"immediate-{Guid.NewGuid()}";
            try
            {
                var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
                    scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromMinutes(5))
                {
                    StartImmediatelyIfLate = true,
                    OrchestrationInput = "immediate"
                });

                var desc = await client.DescribeAsync();
                Assert.NotNull(desc.LastRunAt);
                Assert.NotNull(desc.NextRunAt);
            }
            finally
            {
                await CleanupSchedule(scheduleId);
            }
        }

        [Fact]
        public async Task Schedule_ShouldHandleShortIntervals()
        {
            var scheduleId = $"short-interval-{Guid.NewGuid()}";
            try
            {
                var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
                    scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromSeconds(5))
                {
                    StartImmediatelyIfLate = true,
                    OrchestrationInput = "short"
                });

                await Task.Delay(TimeSpan.FromSeconds(12));
                var desc = await client.DescribeAsync();
                Assert.True(desc.LastRunAt.HasValue);
            }
            finally
            {
                await CleanupSchedule(scheduleId);
            }
        }

        [Fact]
        public async Task Schedule_ShouldHandleLongIntervals()
        {
            var scheduleId = $"long-interval-{Guid.NewGuid()}";
            try
            {
                var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
                    scheduleId, nameof(LongRunningOrchestrator), TimeSpan.FromMinutes(10))
                {
                    StartImmediatelyIfLate = true,
                    OrchestrationInput = "long"
                });

                var desc = await client.DescribeAsync();
                Assert.Equal(TimeSpan.FromMinutes(10), desc.Interval);
            }
            finally
            {
                await CleanupSchedule(scheduleId);
            }
        }

        [Fact]
        public async Task Schedule_ShouldHandlePauseResume()
        {
            var scheduleId = $"pause-resume-{Guid.NewGuid()}";
            try
            {
                var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
                    scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromSeconds(10))
                {
                    StartImmediatelyIfLate = true,
                    OrchestrationInput = "pause-test"
                });

                await client.PauseAsync();
                var desc = await client.DescribeAsync();
                Assert.Equal(ScheduleStatus.Paused, desc.Status);

                await client.ResumeAsync();
                desc = await client.DescribeAsync();
                Assert.Equal(ScheduleStatus.Active, desc.Status);
            }
            finally
            {
                await CleanupSchedule(scheduleId);
            }
        }

        [Fact]
        public async Task Schedule_ShouldHandleUpdate()
        {
            var scheduleId = $"update-{Guid.NewGuid()}";
            try
            {
                var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
                    scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromMinutes(1))
                {
                    OrchestrationInput = "original"
                });

                await client.UpdateAsync(new ScheduleUpdateOptions
                {
                    OrchestrationInput = "updated",
                    Interval = TimeSpan.FromMinutes(2)
                });

                var desc = await client.DescribeAsync();
                Assert.Equal("updated", desc.OrchestrationInput);
                Assert.Equal(TimeSpan.FromMinutes(2), desc.Interval);
            }
            finally
            {
                await CleanupSchedule(scheduleId);
            }
        }

        [Fact]
        public async Task Schedule_ShouldHandleMultipleUpdates()
        {
            var scheduleId = $"multi-update-{Guid.NewGuid()}";
            try
            {
                var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
                    scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromMinutes(1)));

                for (int i = 1; i <= 3; i++)
                {
                    await client.UpdateAsync(new ScheduleUpdateOptions
                    {
                        OrchestrationInput = $"update-{i}",
                        Interval = TimeSpan.FromMinutes(i)
                    });
                }

                var desc = await client.DescribeAsync();
                Assert.Equal("update-3", desc.OrchestrationInput);
                Assert.Equal(TimeSpan.FromMinutes(3), desc.Interval);
            }
            finally
            {
                await CleanupSchedule(scheduleId);
            }
        }

        [Fact]
        public async Task Schedule_ShouldHandleParallelCreation()
        {
            var scheduleIds = new List<string>();
            try
            {
                var tasks = new List<Task<ScheduleClient>>();
                for (int i = 0; i < 5; i++)
                {
                    var id = $"parallel-{Guid.NewGuid()}";
                    scheduleIds.Add(id);
                    tasks.Add(this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
                        id, nameof(SimpleOrchestrator), TimeSpan.FromMinutes(1))
                    {
                        OrchestrationInput = $"parallel-{i}"
                    }));
                }

                await Task.WhenAll(tasks);
                foreach (var id in scheduleIds)
                {
                    var client = this.ScheduledTaskClient.GetScheduleClient(id);
                    var desc = await client.DescribeAsync();
                    Assert.Equal(ScheduleStatus.Active, desc.Status);
                }
            }
            finally
            {
                foreach (var id in scheduleIds)
                {
                    await CleanupSchedule(id);
                }
            }
        }

        [Fact]
        public async Task Schedule_ShouldHandleListSchedules()
        {
            var scheduleIds = new List<string>();
            try
            {
                // Create multiple schedules
                for (int i = 0; i < 3; i++)
                {
                    var id = $"list-test-{Guid.NewGuid()}";
                    scheduleIds.Add(id);
                    await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
                        id, nameof(SimpleOrchestrator), TimeSpan.FromMinutes(1)));
                }

                var schedules = this.ScheduledTaskClient.ListSchedulesAsync();
                var count = 0;
                await foreach (var schedule in schedules)
                {
                    if (scheduleIds.Contains(schedule.ScheduleId))
                    {
                        count++;
                    }
                }
                Assert.Equal(scheduleIds.Count, count);
            }
            finally
            {
                foreach (var id in scheduleIds)
                {
                    await CleanupSchedule(id);
                }
            }
        }

        [Fact]
        public async Task Schedule_ShouldHandleGetSchedule()
        {
            var scheduleId = $"get-schedule-{Guid.NewGuid()}";
            try
            {
                await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
                    scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromMinutes(1)));

                var desc = await this.ScheduledTaskClient.GetScheduleAsync(scheduleId);
                Assert.NotNull(desc);
                Assert.Equal(scheduleId, desc.ScheduleId);
            }
            finally
            {
                await CleanupSchedule(scheduleId);
            }
        }

        [Fact]
        public async Task Schedule_ShouldHandleNonExistentSchedule()
        {
            var desc = await this.ScheduledTaskClient.GetScheduleAsync($"non-existent-{Guid.NewGuid()}");
            Assert.Null(desc);
        }

        [Fact]
        public async Task Schedule_ShouldHandleDeletedSchedule()
        {
            var scheduleId = $"delete-test-{Guid.NewGuid()}";
            var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
                scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromMinutes(1)));

            await client.DeleteAsync();
            var desc = await this.ScheduledTaskClient.GetScheduleAsync(scheduleId);
            Assert.Null(desc);
        }

        [Fact]
        public async Task Schedule_ShouldHandleExceptionInOrchestration()
        {
            var scheduleId = $"exception-{Guid.NewGuid()}";
            try
            {
                var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
                    scheduleId, nameof(AlwaysThrowExceptionOrchestrator), TimeSpan.FromSeconds(5))
                {
                    StartImmediatelyIfLate = true
                });

                await Task.Delay(TimeSpan.FromSeconds(10));
                var desc = await client.DescribeAsync();
                Assert.NotNull(desc.LastRunAt);
                Assert.Equal(ScheduleStatus.Active, desc.Status);
            }
            finally
            {
                await CleanupSchedule(scheduleId);
            }
        }

        [Fact]
        public async Task Schedule_ShouldHandleRandomExecutionTimes()
        {
            var scheduleId = $"random-time-{Guid.NewGuid()}";
            try
            {
                var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
                    scheduleId, nameof(RandomRunTimeOrchestrator), TimeSpan.FromSeconds(10))
                {
                    StartImmediatelyIfLate = true
                });

                await Task.Delay(TimeSpan.FromSeconds(25));
                var desc = await client.DescribeAsync();
                Assert.True(desc.LastRunAt.HasValue);
            }
            finally
            {
                await CleanupSchedule(scheduleId);
            }
        }

        [Fact]
        public async Task Schedule_ShouldHandleMinimumInterval()
        {
            var scheduleId = $"min-interval-{Guid.NewGuid()}";
            try
            {
                var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
                    scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromSeconds(1))
                {
                    StartImmediatelyIfLate = true
                });

                var desc = await client.DescribeAsync();
                Assert.Equal(TimeSpan.FromSeconds(1), desc.Interval);
            }
            finally
            {
                await CleanupSchedule(scheduleId);
            }
        }

        [Fact]
        public async Task Schedule_ShouldHandleMaximumInterval()
        {
            var scheduleId = $"max-interval-{Guid.NewGuid()}";
            try
            {
                var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
                    scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromDays(1))
                {
                    StartImmediatelyIfLate = true
                });

                var desc = await client.DescribeAsync();
                Assert.Equal(TimeSpan.FromDays(1), desc.Interval);
            }
            finally
            {
                await CleanupSchedule(scheduleId);
            }
        }

        [Fact]
        public async Task Schedule_ShouldHandleParallelOperations()
        {
            var scheduleId = $"parallel-ops-{Guid.NewGuid()}";
            try
            {
                var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
                    scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromSeconds(5))
                {
                    StartImmediatelyIfLate = true
                });

                // Perform multiple operations in parallel
                await Task.WhenAll(
                    client.PauseAsync(),
                    client.DescribeAsync(),
                    client.UpdateAsync(new ScheduleUpdateOptions { OrchestrationInput = "parallel" })
                );

                var desc = await client.DescribeAsync();
                Assert.Equal(ScheduleStatus.Paused, desc.Status);
                Assert.Equal("parallel", desc.OrchestrationInput);
            }
            finally
            {
                await CleanupSchedule(scheduleId);
            }
        }

        [Fact]
        public async Task Schedule_ShouldHandleUpdateWhilePaused()
        {
            var scheduleId = $"update-paused-{Guid.NewGuid()}";
            try
            {
                var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
                    scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromMinutes(1)));

                await client.PauseAsync();
                await client.UpdateAsync(new ScheduleUpdateOptions
                {
                    OrchestrationInput = "updated-while-paused",
                    Interval = TimeSpan.FromMinutes(2)
                });

                var desc = await client.DescribeAsync();
                Assert.Equal(ScheduleStatus.Paused, desc.Status);
                Assert.Equal("updated-while-paused", desc.OrchestrationInput);
                Assert.Equal(TimeSpan.FromMinutes(2), desc.Interval);
            }
            finally
            {
                await CleanupSchedule(scheduleId);
            }
        }

        private async Task CleanupSchedule(string scheduleId)
        {
            try
            {
                var client = this.ScheduledTaskClient.GetScheduleClient(scheduleId);
                await client.DeleteAsync();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}