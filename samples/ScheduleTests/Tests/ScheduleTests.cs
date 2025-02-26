// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.ScheduledTasks;
using ScheduleTests.Infrastructure;
using ScheduleTests.Tasks;
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
                var startTime = DateTimeOffset.UtcNow;
                var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
                    scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromMinutes(5))
                {
                    StartAt = startTime,
                    EndAt = startTime.AddMinutes(1),
                    OrchestrationInput = scheduleId,
                    StartImmediatelyIfLate = true
                });

                await Task.Delay(TimeSpan.FromSeconds(3));
                ScheduleDescription desc = await client.DescribeAsync();
                Assert.Equal(ScheduleStatus.Active, desc.Status);
                // assert lastrunat is within 1 second of start time
                Assert.True(desc.LastRunAt >= startTime && desc.LastRunAt <= startTime.AddSeconds(3), $"lastrunat should be within 3 seconds of start time, but is {desc.LastRunAt} and start time is {startTime}");
                Assert.Equal(startTime.AddMinutes(1), desc.EndAt);
                Assert.Equal(scheduleId, desc.OrchestrationInput);
                Assert.Equal(ScheduleStatus.Active, desc.Status);
                // assert orch name
                Assert.Equal(nameof(SimpleOrchestrator), desc.OrchestrationName);
                // assert schedule id
                Assert.Equal(scheduleId, desc.ScheduleId);

                // get all orchestration instances
                var count = await this.GetOrchInstancesCount(scheduleId, nameof(SimpleOrchestrator));
                Assert.Equal(1, count);
            }
            finally
            {
                await this.CleanupSchedule(scheduleId);
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
                await this.CleanupSchedule(scheduleId);
            }
        }

        [Fact]
        public async Task Schedule_ShouldRespectEndTime()
        {
            var scheduleId = $"end-time-{Guid.NewGuid()}";
            try
            {
                var startTime = DateTimeOffset.UtcNow;
                var endTime = startTime.AddSeconds(5);
                var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
                    scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromSeconds(2))
                {
                    StartAt = startTime,
                    EndAt = endTime,
                    OrchestrationInput = scheduleId
                });

                await Task.Delay(TimeSpan.FromSeconds(5));
                var count = await this.GetOrchInstancesCount(scheduleId, nameof(SimpleOrchestrator));
                var desc = await client.DescribeAsync();
                Assert.Equal(ScheduleStatus.Active, desc.Status);

                await Task.Delay(TimeSpan.FromSeconds(5));
                var count2 = await this.GetOrchInstancesCount(scheduleId, nameof(SimpleOrchestrator));
                Assert.Equal(count, count2);
            }
            finally
            {
                await this.CleanupSchedule(scheduleId);
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
                await this.CleanupSchedule(scheduleId);
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
                await this.CleanupSchedule(scheduleId);
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
                    StartImmediatelyIfLate = false,
                    OrchestrationInput = "long"
                });

                var desc = await client.DescribeAsync();
                Assert.Equal(TimeSpan.FromMinutes(10), desc.Interval);
                Assert.Equal(ScheduleStatus.Active, desc.Status);
                Assert.Null(desc.LastRunAt);
            }
            finally
            {
                await this.CleanupSchedule(scheduleId);
            }
        }

        [Fact]
        public async Task Schedule_ShouldHandlePauseResume()
        {
            var scheduleId = $"pause-resume-{Guid.NewGuid()}";
            try
            {
                var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
                    scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromSeconds(2))
                {
                    StartImmediatelyIfLate = true,
                    OrchestrationInput = scheduleId
                });

                await Task.Delay(TimeSpan.FromSeconds(10));

                // count num of orch instances input == scheduleId
                var count1 = await this.GetOrchInstancesCount(scheduleId, nameof(SimpleOrchestrator));

                await client.PauseAsync();
                var desc = await client.DescribeAsync();
                Assert.Equal(ScheduleStatus.Paused, desc.Status);

                // count num of orch instances input == scheduleId
                var count2 = await this.GetOrchInstancesCount(scheduleId, nameof(SimpleOrchestrator));
                Assert.Equal(count1, count2);
                await client.ResumeAsync();
                desc = await client.DescribeAsync();
                Assert.Equal(ScheduleStatus.Active, desc.Status);
            }
            finally
            {
                await this.CleanupSchedule(scheduleId);
            }
        }

        [Fact]
        public async Task Schedule_ShouldHandleUpdate()
        {
            var scheduleId = $"update-{Guid.NewGuid()}";
            try
            {
                var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
                    scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromSeconds(2))
                {
                    OrchestrationInput = scheduleId
                });

                await Task.Delay(TimeSpan.FromSeconds(5));
                // get and assert input and interval
                var desc = await client.DescribeAsync();
                Assert.Equal(scheduleId, desc.OrchestrationInput);
                Assert.Equal(TimeSpan.FromSeconds(2), desc.Interval);

                // get orch instances input == original
                var count1 = await this.GetOrchInstancesCount(scheduleId, nameof(SimpleOrchestrator));
                Assert.True(count1 > 0, $"count1 should be greater than 0, but is {count1}");

                var newInput = $"updated-{Guid.NewGuid()}";

                await client.UpdateAsync(new ScheduleUpdateOptions
                {
                    OrchestrationInput = newInput,
                    Interval = TimeSpan.FromSeconds(3)
                });

                desc = await client.DescribeAsync();
                Assert.Equal(newInput, desc.OrchestrationInput);
                Assert.Equal(TimeSpan.FromSeconds(3), desc.Interval);

                await Task.Delay(TimeSpan.FromSeconds(5));
                // get orch instances input == updated
                var count2 = await this.GetOrchInstancesCount(newInput, nameof(SimpleOrchestrator));
                Assert.True(count2 > 0, $"count2 should be greater than 0, but is {count2}");

                // get orch instances input == newInput
                var count3 = await this.GetOrchInstancesCount(newInput, nameof(SimpleOrchestrator));
                // assert descriptively
                Assert.True(count3 > 0, $"count3 should be greater than 0, but is {count3}");
            }
            finally
            {
                await this.CleanupSchedule(scheduleId);
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

                var startAt = DateTimeOffset.UtcNow;
                var endAt = startAt.AddMinutes(4);

                for (int i = 1; i <= 3; i++)
                {
                    await client.UpdateAsync(new ScheduleUpdateOptions
                    {
                        OrchestrationInput = $"update-{i}",
                        Interval = TimeSpan.FromMinutes(i),
                        StartAt = startAt.AddMinutes(i),
                        EndAt = endAt.AddMinutes(i)
                    });
                }

                var desc = await client.DescribeAsync();
                Assert.Equal("update-3", desc.OrchestrationInput);
                Assert.Equal(TimeSpan.FromMinutes(3), desc.Interval);
                Assert.Equal(startAt.AddMinutes(3), desc.StartAt);
                Assert.Equal(endAt.AddMinutes(3), desc.EndAt);
            }
            finally
            {
                await this.CleanupSchedule(scheduleId);
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
                    await this.CleanupSchedule(id);
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
                    await this.CleanupSchedule(id);
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
                await this.CleanupSchedule(scheduleId);
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
                    scheduleId, nameof(AlwaysThrowExceptionOrchestrator), TimeSpan.FromSeconds(1))
                {
                    StartImmediatelyIfLate = true,
                    OrchestrationInput = scheduleId
                });

                await Task.Delay(TimeSpan.FromSeconds(5));
                var desc = await client.DescribeAsync();
                Assert.NotNull(desc.LastRunAt);
                Assert.Equal(ScheduleStatus.Active, desc.Status);

                // get orch instances input == scheduleId
                var count = await this.GetOrchInstancesCount(scheduleId, nameof(AlwaysThrowExceptionOrchestrator));
                Assert.True(count > 0, $"count should be greater than 0, but is {count}");
            }
            finally
            {
                await this.CleanupSchedule(scheduleId);
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
                    StartImmediatelyIfLate = true,
                    OrchestrationInput = scheduleId
                });

                await Task.Delay(TimeSpan.FromSeconds(25));
                var desc = await client.DescribeAsync();
                Assert.True(desc.LastRunAt.HasValue);

                // get orch instances input == scheduleId
                var instances = this.Client.GetAllInstancesAsync(new OrchestrationQuery()
                {
                    CreatedFrom = DateTimeOffset.UtcNow.AddMinutes(-1),
                    FetchInputsAndOutputs = true
                });
                var count = 0;
                await foreach (var instance in instances)
                {
                    if (instance.Name == nameof(RandomRunTimeOrchestrator))
                    {
                        var input = instance.ReadInputAs<string>();
                        if (input == scheduleId)
                        {
                            count++;
                        }
                    }
                }
                Assert.True(count == 3, $"count should be 3, but is {count}");
            }
            finally
            {
                await this.CleanupSchedule(scheduleId);
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
                    StartImmediatelyIfLate = true,
                    OrchestrationInput = scheduleId
                });

                var desc = await client.DescribeAsync();
                Assert.Equal(TimeSpan.FromSeconds(1), desc.Interval);

                // get orch instances input == scheduleId
                var count = await this.GetOrchInstancesCount(scheduleId, nameof(SimpleOrchestrator));
                Assert.True(count > 0, $"count should be greater than 0, but is {count}");
            }
            finally
            {
                await this.CleanupSchedule(scheduleId);
            }
        }

        [Fact]
        public async Task Schedule_ShouldHandleMaximumInterval()
        {
            var scheduleId = $"max-interval-{Guid.NewGuid()}";
            try
            {
                var startTime = DateTimeOffset.UtcNow;
                var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
                    scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromDays(1))
                {
                    StartAt = startTime,
                    StartImmediatelyIfLate = true
                });

                var desc = await client.DescribeAsync();
                Assert.Equal(TimeSpan.FromDays(1), desc.Interval);
                Assert.Equal(startTime.AddDays(1), desc.NextRunAt);
            }
            finally
            {
                await this.CleanupSchedule(scheduleId);
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
                await this.CleanupSchedule(scheduleId);
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
                await this.CleanupSchedule(scheduleId);
            }
        }

        async Task CleanupSchedule(string scheduleId)
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

        // helper function for getting orch instances input == scheduleId
        async Task<int> GetOrchInstancesCount(string orchInput, string orchName)
        {
            var instances = this.Client.GetAllInstancesAsync(new OrchestrationQuery()
            {
                CreatedFrom = DateTimeOffset.UtcNow.AddMinutes(-1),
                FetchInputsAndOutputs = true
            });
            var count = 0;
            await foreach (var instance in instances)
            {
                if (instance.Name == orchName)
                {
                    var input = instance.ReadInputAs<string>();
                    if (input == orchInput)
                    {
                        count++;
                    }
                }
            }
            return count;
        }
    }
}