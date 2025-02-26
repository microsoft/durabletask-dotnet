// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
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
                // assert lastrunat is within 3 second of start time, log both time if failed to millisecond level
                Assert.True(desc.LastRunAt >= startTime && desc.LastRunAt <= startTime.AddSeconds(3), $"lastrunat should be within 3 seconds of start time, but is {desc.LastRunAt:yyyy-MM-dd HH:mm:ss.fff} and start time is {startTime:yyyy-MM-dd HH:mm:ss.fff}");
                Assert.Equal(startTime.AddMinutes(1), desc.EndAt);
                Assert.Equal(scheduleId, desc.OrchestrationInput);
                Assert.Equal(ScheduleStatus.Active, desc.Status);
                // assert orch name
                Assert.Equal(nameof(SimpleOrchestrator), desc.OrchestrationName);
                // assert schedule id
                Assert.Equal(scheduleId, desc.ScheduleId);

                // get all orchestration instances
                var instances = this.GetOrchInstances(scheduleId);
                Assert.Equal(1, await this.GetCountFromPageable(instances));

                // get all orchestration scheduled times
                var instanceIds = await this.GetInstanceIdsFromPageable(instances);
                var scheduledTimes = this.GetOrchestrationScheduledTimes(instanceIds);
                Assert.Single(scheduledTimes, "should be only one instance");
                // assert scheduled time is within 3 seconds of start time
                Assert.True(scheduledTimes[0] >= startTime && scheduledTimes[0] <= startTime.AddSeconds(3), $"scheduled time should be within 3 seconds of start time, but is {scheduledTimes[0]:yyyy-MM-dd HH:mm:ss.fff} and start time is {startTime:yyyy-MM-dd HH:mm:ss.fff}");
            }
            finally
            {
                await this.CleanupSchedule(scheduleId);
            }
        }

        // [Fact]
        // public async Task Schedule_ShouldRespectStartTime()
        // {
        //     var scheduleId = $"start-time-{Guid.NewGuid()}";
        //     try
        //     {
        //         var startTime = DateTimeOffset.UtcNow.AddSeconds(30);
        //         var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //             scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromMinutes(1))
        //         {
        //             StartAt = startTime,
        //             OrchestrationInput = "delayed-start"
        //         });

        //         // Check immediately - should not be started
        //         var desc = await client.DescribeAsync();
        //         Assert.Equal(startTime, desc.NextRunAt);
        //     }
        //     finally
        //     {
        //         await this.CleanupSchedule(scheduleId);
        //     }
        // }

        // [Fact]
        // public async Task Schedule_ShouldRespectEndTime()
        // {
        //     var scheduleId = $"end-time-{Guid.NewGuid()}";
        //     try
        //     {
        //         var startTime = DateTimeOffset.UtcNow;
        //         var endTime = startTime.AddSeconds(5);
        //         var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //             scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromSeconds(2))
        //         {
        //             StartAt = startTime,
        //             EndAt = endTime,
        //             OrchestrationInput = scheduleId
        //         });

        //         await Task.Delay(TimeSpan.FromSeconds(5));
        //         var count = await this.GetOrchInstancesCount(scheduleId);
        //         var desc = await client.DescribeAsync();
        //         Assert.Equal(ScheduleStatus.Active, desc.Status);

        //         await Task.Delay(TimeSpan.FromSeconds(5));
        //         var count2 = await this.GetOrchInstancesCount(scheduleId);
        //         Assert.Equal(count, count2);
        //     }
        //     finally
        //     {
        //         await this.CleanupSchedule(scheduleId);
        //     }
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandleImmediateStart()
        // {
        //     var scheduleId = $"immediate-{Guid.NewGuid()}";
        //     try
        //     {
        //         var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //             scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromMinutes(5))
        //         {
        //             StartImmediatelyIfLate = true,
        //             OrchestrationInput = "immediate"
        //         });

        //         var desc = await client.DescribeAsync();
        //         Assert.NotNull(desc.LastRunAt);
        //         Assert.NotNull(desc.NextRunAt);
        //     }
        //     finally
        //     {
        //         await this.CleanupSchedule(scheduleId);
        //     }
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandleShortIntervals()
        // {
        //     var scheduleId = $"short-interval-{Guid.NewGuid()}";
        //     try
        //     {
        //         var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //             scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromSeconds(5))
        //         {
        //             StartImmediatelyIfLate = true,
        //             OrchestrationInput = "short"
        //         });

        //         await Task.Delay(TimeSpan.FromSeconds(12));
        //         var desc = await client.DescribeAsync();
        //         Assert.True(desc.LastRunAt.HasValue);
        //     }
        //     finally
        //     {
        //         await this.CleanupSchedule(scheduleId);
        //     }
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandleLongIntervals()
        // {
        //     var scheduleId = $"long-interval-{Guid.NewGuid()}";
        //     try
        //     {
        //         var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //             scheduleId, nameof(LongRunningOrchestrator), TimeSpan.FromMinutes(10))
        //         {
        //             StartImmediatelyIfLate = false,
        //             OrchestrationInput = "long"
        //         });

        //         var desc = await client.DescribeAsync();
        //         Assert.Equal(TimeSpan.FromMinutes(10), desc.Interval);
        //         Assert.Equal(ScheduleStatus.Active, desc.Status);
        //         Assert.Null(desc.LastRunAt);
        //     }
        //     finally
        //     {
        //         await this.CleanupSchedule(scheduleId);
        //     }
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandlePauseResume()
        // {
        //     var scheduleId = $"pause-resume-{Guid.NewGuid()}";
        //     try
        //     {
        //         var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //             scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromSeconds(2))
        //         {
        //             StartImmediatelyIfLate = true,
        //             OrchestrationInput = scheduleId
        //         });

        //         await Task.Delay(TimeSpan.FromSeconds(10));

        //         // count num of orch instances input == scheduleId
        //         var count1 = await this.GetOrchInstancesCount(scheduleId);

        //         await client.PauseAsync();
        //         var desc = await client.DescribeAsync();
        //         Assert.Equal(ScheduleStatus.Paused, desc.Status);

        //         // count num of orch instances input == scheduleId
        //         var count2 = await this.GetOrchInstancesCount(scheduleId);
        //         Assert.Equal(count1, count2);
        //         await client.ResumeAsync();
        //         desc = await client.DescribeAsync();
        //         Assert.Equal(ScheduleStatus.Active, desc.Status);
        //     }
        //     finally
        //     {
        //         await this.CleanupSchedule(scheduleId);
        //     }
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandleUpdate()
        // {
        //     var scheduleId = $"update-{Guid.NewGuid()}";
        //     try
        //     {
        //         var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //             scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromSeconds(2))
        //         {
        //             OrchestrationInput = scheduleId
        //         });

        //         await Task.Delay(TimeSpan.FromSeconds(5));
        //         // get and assert input and interval
        //         var desc = await client.DescribeAsync();
        //         Assert.Equal(scheduleId, desc.OrchestrationInput);
        //         Assert.Equal(TimeSpan.FromSeconds(2), desc.Interval);

        //         // get orch instances input == original
        //         var count1 = await this.GetOrchInstancesCount(scheduleId);
        //         Assert.True(count1 > 0, $"count1 should be greater than 0, but is {count1}");

        //         var newInput = $"updated-{Guid.NewGuid()}";

        //         await client.UpdateAsync(new ScheduleUpdateOptions
        //         {
        //             OrchestrationInput = newInput,
        //             Interval = TimeSpan.FromSeconds(3)
        //         });

        //         desc = await client.DescribeAsync();
        //         Assert.Equal(newInput, desc.OrchestrationInput);
        //         Assert.Equal(TimeSpan.FromSeconds(3), desc.Interval);

        //         await Task.Delay(TimeSpan.FromSeconds(5));
        //         // get orch instances input == updated
        //         var count2 = await this.GetOrchInstancesCount(newInput);
        //         Assert.True(count2 > 0, $"count2 should be greater than 0, but is {count2}");

        //         // get orch instances input == newInput
        //         var count3 = await this.GetOrchInstancesCount(newInput);
        //         // assert descriptively
        //         Assert.True(count3 > 0, $"count3 should be greater than 0, but is {count3}");
        //     }
        //     finally
        //     {
        //         await this.CleanupSchedule(scheduleId);
        //     }
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandleMultipleUpdates()
        // {
        //     var scheduleId = $"multi-update-{Guid.NewGuid()}";
        //     try
        //     {
        //         var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //             scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromMinutes(1)));

        //         var startAt = DateTimeOffset.UtcNow;
        //         var endAt = startAt.AddMinutes(4);

        //         for (int i = 1; i <= 3; i++)
        //         {
        //             await client.UpdateAsync(new ScheduleUpdateOptions
        //             {
        //                 OrchestrationInput = $"update-{i}",
        //                 Interval = TimeSpan.FromMinutes(i),
        //                 StartAt = startAt.AddMinutes(i),
        //                 EndAt = endAt.AddMinutes(i)
        //             });
        //         }

        //         var desc = await client.DescribeAsync();
        //         Assert.Equal("update-3", desc.OrchestrationInput);
        //         Assert.Equal(TimeSpan.FromMinutes(3), desc.Interval);
        //         Assert.Equal(startAt.AddMinutes(3), desc.StartAt);
        //         Assert.Equal(endAt.AddMinutes(3), desc.EndAt);
        //     }
        //     finally
        //     {
        //         await this.CleanupSchedule(scheduleId);
        //     }
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandleParallelCreation()
        // {
        //     var scheduleIds = new List<string>();
        //     try
        //     {
        //         var tasks = new List<Task<ScheduleClient>>();
        //         for (int i = 0; i < 5; i++)
        //         {
        //             var id = $"parallel-{Guid.NewGuid()}";
        //             scheduleIds.Add(id);
        //             tasks.Add(this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //                 id, nameof(SimpleOrchestrator), TimeSpan.FromMinutes(1))
        //             {
        //                 OrchestrationInput = $"parallel-{i}"
        //             }));
        //         }

        //         await Task.WhenAll(tasks);
        //         foreach (var id in scheduleIds)
        //         {
        //             var client = this.ScheduledTaskClient.GetScheduleClient(id);
        //             var desc = await client.DescribeAsync();
        //             Assert.Equal(ScheduleStatus.Active, desc.Status);
        //         }
        //     }
        //     finally
        //     {
        //         foreach (var id in scheduleIds)
        //         {
        //             await this.CleanupSchedule(id);
        //         }
        //     }
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandleListSchedules()
        // {
        //     var scheduleIds = new List<string>();
        //     try
        //     {
        //         // Create multiple schedules
        //         for (int i = 0; i < 3; i++)
        //         {
        //             var id = $"list-test-{Guid.NewGuid()}";
        //             scheduleIds.Add(id);
        //             await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //                 id, nameof(SimpleOrchestrator), TimeSpan.FromMinutes(1)));
        //         }

        //         var schedules = this.ScheduledTaskClient.ListSchedulesAsync();
        //         var count = 0;
        //         await foreach (var schedule in schedules)
        //         {
        //             if (scheduleIds.Contains(schedule.ScheduleId))
        //             {
        //                 count++;
        //             }
        //         }
        //         Assert.Equal(scheduleIds.Count, count);
        //     }
        //     finally
        //     {
        //         foreach (var id in scheduleIds)
        //         {
        //             await this.CleanupSchedule(id);
        //         }
        //     }
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandleGetSchedule()
        // {
        //     var scheduleId = $"get-schedule-{Guid.NewGuid()}";
        //     try
        //     {
        //         await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //             scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromMinutes(1)));

        //         var desc = await this.ScheduledTaskClient.GetScheduleAsync(scheduleId);
        //         Assert.NotNull(desc);
        //         Assert.Equal(scheduleId, desc.ScheduleId);
        //     }
        //     finally
        //     {
        //         await this.CleanupSchedule(scheduleId);
        //     }
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandleNonExistentSchedule()
        // {
        //     var desc = await this.ScheduledTaskClient.GetScheduleAsync($"non-existent-{Guid.NewGuid()}");
        //     Assert.Null(desc);
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandleDeletedSchedule()
        // {
        //     var scheduleId = $"delete-test-{Guid.NewGuid()}";
        //     var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //         scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromMinutes(1)));

        //     await client.DeleteAsync();
        //     var desc = await this.ScheduledTaskClient.GetScheduleAsync(scheduleId);
        //     Assert.Null(desc);
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandleExceptionInOrchestration()
        // {
        //     var scheduleId = $"exception-{Guid.NewGuid()}";
        //     try
        //     {
        //         var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //             scheduleId, nameof(AlwaysThrowExceptionOrchestrator), TimeSpan.FromSeconds(1))
        //         {
        //             StartImmediatelyIfLate = true,
        //             OrchestrationInput = scheduleId
        //         });

        //         await Task.Delay(TimeSpan.FromSeconds(5));
        //         var desc = await client.DescribeAsync();
        //         Assert.NotNull(desc.LastRunAt);
        //         Assert.Equal(ScheduleStatus.Active, desc.Status);

        //         // get orch instances input == scheduleId
        //         var count = await this.GetOrchInstancesCount(scheduleId);
        //         Assert.True(count > 0, $"count should be greater than 0, but is {count}");
        //     }
        //     finally
        //     {
        //         await this.CleanupSchedule(scheduleId);
        //     }
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandleRandomExecutionTimes()
        // {
        //     var scheduleId = $"random-time-{Guid.NewGuid()}";
        //     try
        //     {
        //         var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //             scheduleId, nameof(RandomRunTimeOrchestrator), TimeSpan.FromSeconds(10))
        //         {
        //             StartImmediatelyIfLate = true,
        //             OrchestrationInput = scheduleId
        //         });

        //         await Task.Delay(TimeSpan.FromSeconds(25));
        //         var desc = await client.DescribeAsync();
        //         Assert.True(desc.LastRunAt.HasValue);

        //         // get orch instances input == scheduleId
        //         var instances = this.Client.GetAllInstancesAsync(new OrchestrationQuery()
        //         {
        //             CreatedFrom = DateTimeOffset.UtcNow.AddMinutes(-1),
        //             FetchInputsAndOutputs = true
        //         });
        //         var count = 0;
        //         await foreach (var instance in instances)
        //         {
        //             if (instance.Name == nameof(RandomRunTimeOrchestrator))
        //             {
        //                 var input = instance.ReadInputAs<string>();
        //                 if (input == scheduleId)
        //                 {
        //                     count++;
        //                 }
        //             }
        //         }
        //         Assert.True(count == 3, $"count should be 3, but is {count}");
        //     }
        //     finally
        //     {
        //         await this.CleanupSchedule(scheduleId);
        //     }
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandleMinimumInterval()
        // {
        //     var scheduleId = $"min-interval-{Guid.NewGuid()}";
        //     try
        //     {
        //         var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //             scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromSeconds(1))
        //         {
        //             StartImmediatelyIfLate = true,
        //             OrchestrationInput = scheduleId
        //         });

        //         var desc = await client.DescribeAsync();
        //         Assert.Equal(TimeSpan.FromSeconds(1), desc.Interval);

        //         // get orch instances input == scheduleId
        //         var count = await this.GetOrchInstancesCount(scheduleId);
        //         Assert.True(count > 0, $"count should be greater than 0, but is {count}");
        //     }
        //     finally
        //     {
        //         await this.CleanupSchedule(scheduleId);
        //     }
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandleMaximumInterval()
        // {
        //     var scheduleId = $"max-interval-{Guid.NewGuid()}";
        //     try
        //     {
        //         var startTime = DateTimeOffset.UtcNow;
        //         var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //             scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromDays(1))
        //         {
        //             StartAt = startTime,
        //             StartImmediatelyIfLate = true
        //         });

        //         var desc = await client.DescribeAsync();
        //         Assert.Equal(TimeSpan.FromDays(1), desc.Interval);
        //         Assert.Equal(startTime.AddDays(1), desc.NextRunAt);
        //     }
        //     finally
        //     {
        //         await this.CleanupSchedule(scheduleId);
        //     }
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandleParallelOperations()
        // {
        //     var scheduleId = $"parallel-ops-{Guid.NewGuid()}";
        //     try
        //     {
        //         var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //             scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromSeconds(5))
        //         {
        //             StartImmediatelyIfLate = true
        //         });

        //         // Perform multiple operations in parallel
        //         await Task.WhenAll(
        //             client.PauseAsync(),
        //             client.DescribeAsync(),
        //             client.UpdateAsync(new ScheduleUpdateOptions { OrchestrationInput = "parallel" })
        //         );

        //         var desc = await client.DescribeAsync();
        //         Assert.Equal(ScheduleStatus.Paused, desc.Status);
        //         Assert.Equal("parallel", desc.OrchestrationInput);
        //     }
        //     finally
        //     {
        //         await this.CleanupSchedule(scheduleId);
        //     }
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandleUpdateWhilePaused()
        // {
        //     var scheduleId = $"update-paused-{Guid.NewGuid()}";
        //     try
        //     {
        //         var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //             scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromMinutes(1)));

        //         await client.PauseAsync();
        //         await client.UpdateAsync(new ScheduleUpdateOptions
        //         {
        //             OrchestrationInput = "updated-while-paused",
        //             Interval = TimeSpan.FromMinutes(2)
        //         });

        //         var desc = await client.DescribeAsync();
        //         Assert.Equal(ScheduleStatus.Paused, desc.Status);
        //         Assert.Equal("updated-while-paused", desc.OrchestrationInput);
        //         Assert.Equal(TimeSpan.FromMinutes(2), desc.Interval);
        //     }
        //     finally
        //     {
        //         await this.CleanupSchedule(scheduleId);
        //     }
        // }


        // [Fact]
        // public async Task Schedule_ShouldHandleRecoveryAfterSystemFailure()
        // {
        //     var scheduleId = $"recovery-{Guid.NewGuid()}";
        //     try
        //     {
        //         var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //             scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromSeconds(5))
        //         {
        //             StartImmediatelyIfLate = true,
        //             OrchestrationInput = "recovery-test"
        //         });

        //         // Simulate system interruption by pausing and resuming quickly
        //         await client.PauseAsync();
        //         await Task.Delay(TimeSpan.FromSeconds(1));
        //         await client.ResumeAsync();

        //         await Task.Delay(TimeSpan.FromSeconds(6));
        //         var desc = await client.DescribeAsync();
        //         Assert.Equal(ScheduleStatus.Active, desc.Status);
        //         Assert.NotNull(desc.LastRunAt);
        //     }
        //     finally
        //     {
        //         await this.CleanupSchedule(scheduleId);
        //     }
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandleGracefulShutdown()
        // {
        //     var scheduleId = $"shutdown-{Guid.NewGuid()}";
        //     try
        //     {
        //         var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //             scheduleId, nameof(LongRunningOrchestrator), TimeSpan.FromSeconds(5))
        //         {
        //             OrchestrationInput = "shutdown-test"
        //         });

        //         // Let it start
        //         await Task.Delay(TimeSpan.FromSeconds(2));

        //         // Request graceful shutdown
        //         await client.PauseAsync();
        //         var desc = await client.DescribeAsync();
        //         Assert.Equal(ScheduleStatus.Paused, desc.Status);
        //     }
        //     finally
        //     {
        //         await this.CleanupSchedule(scheduleId);
        //     }
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandleBackToBackExecution()
        // {
        //     var scheduleId = $"backtoback-{Guid.NewGuid()}";
        //     try
        //     {
        //         var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //             scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromSeconds(1))
        //         {
        //             StartImmediatelyIfLate = true,
        //             OrchestrationInput = scheduleId
        //         });

        //         await Task.Delay(TimeSpan.FromSeconds(5));
        //         var count = await this.GetOrchInstancesCount(scheduleId);
        //         Assert.True(count >= 4, "Should have executed at least 4 times");
        //     }
        //     finally
        //     {
        //         await this.CleanupSchedule(scheduleId);
        //     }
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandleUpdateWithValidation()
        // {
        //     var scheduleId = $"validation-{Guid.NewGuid()}";
        //     try
        //     {
        //         var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //             scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromMinutes(5)));

        //         // Should throw on invalid interval
        //         await Assert.ThrowsAsync<ArgumentException>(async () =>
        //             await client.UpdateAsync(new ScheduleUpdateOptions
        //             {
        //                 Interval = TimeSpan.FromSeconds(-1)
        //             }));

        //         // Should throw on invalid end time
        //         await Assert.ThrowsAsync<ArgumentException>(async () =>
        //             await client.UpdateAsync(new ScheduleUpdateOptions
        //             {
        //                 EndAt = DateTimeOffset.UtcNow.AddDays(-1)
        //             }));
        //     }
        //     finally
        //     {
        //         await this.CleanupSchedule(scheduleId);
        //     }
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandleConcurrentUpdates()
        // {
        //     var scheduleId = $"concurrent-updates-{Guid.NewGuid()}";
        //     try
        //     {
        //         var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //             scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromMinutes(5)));

        //         var updates = new List<Task>();
        //         for (int i = 0; i < 5; i++)
        //         {
        //             updates.Add(client.UpdateAsync(new ScheduleUpdateOptions
        //             {
        //                 OrchestrationInput = $"update-{i}"
        //             }));
        //         }

        //         await Task.WhenAll(updates);
        //         var desc = await client.DescribeAsync();
        //         Assert.Contains("update-", desc.OrchestrationInput);
        //     }
        //     finally
        //     {
        //         await this.CleanupSchedule(scheduleId);
        //     }
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandleUpdateWithNoChanges()
        // {
        //     var scheduleId = $"no-changes-{Guid.NewGuid()}";
        //     try
        //     {
        //         var originalInterval = TimeSpan.FromMinutes(5);
        //         var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //             scheduleId, nameof(SimpleOrchestrator), originalInterval));

        //         await client.UpdateAsync(new ScheduleUpdateOptions
        //         {
        //             Interval = originalInterval
        //         });

        //         var desc = await client.DescribeAsync();
        //         Assert.Equal(originalInterval, desc.Interval);
        //     }
        //     finally
        //     {
        //         await this.CleanupSchedule(scheduleId);
        //     }
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandleImmediateEndTime()
        // {
        //     var scheduleId = $"immediate-end-{Guid.NewGuid()}";
        //     try
        //     {
        //         var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //             scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromMinutes(5))
        //         {
        //             EndAt = DateTimeOffset.UtcNow
        //         });

        //         var desc = await client.DescribeAsync();
        //         Assert.Equal(ScheduleStatus.Paused, desc.Status);
        //     }
        //     finally
        //     {
        //         await this.CleanupSchedule(scheduleId);
        //     }
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandleMaximumConcurrentExecutions()
        // {
        //     var scheduleId = $"max-concurrent-{Guid.NewGuid()}";
        //     try
        //     {
        //         var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //             scheduleId, nameof(LongRunningOrchestrator), TimeSpan.FromSeconds(2))
        //         {
        //             StartImmediatelyIfLate = true,
        //             OrchestrationInput = "concurrent-test"
        //         });

        //         await Task.Delay(TimeSpan.FromSeconds(10));
        //         var desc = await client.DescribeAsync();
        //         Assert.NotNull(desc.LastRunAt);
        //     }
        //     finally
        //     {
        //         await this.CleanupSchedule(scheduleId);
        //     }
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandleUpdateDuringExecution()
        // {
        //     var scheduleId = $"update-during-exec-{Guid.NewGuid()}";
        //     try
        //     {
        //         var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //             scheduleId, nameof(LongRunningOrchestrator), TimeSpan.FromSeconds(5))
        //         {
        //             StartImmediatelyIfLate = true
        //         });

        //         await Task.Delay(TimeSpan.FromSeconds(2));
        //         await client.UpdateAsync(new ScheduleUpdateOptions
        //         {
        //             Interval = TimeSpan.FromSeconds(10)
        //         });

        //         var desc = await client.DescribeAsync();
        //         Assert.Equal(TimeSpan.FromSeconds(10), desc.Interval);
        //     }
        //     finally
        //     {
        //         await this.CleanupSchedule(scheduleId);
        //     }
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandleMultipleStartStopCycles()
        // {
        //     var scheduleId = $"multi-cycle-{Guid.NewGuid()}";
        //     try
        //     {
        //         var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //             scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromSeconds(2)));

        //         for (int i = 0; i < 3; i++)
        //         {
        //             await client.PauseAsync();
        //             var desc1 = await client.DescribeAsync();
        //             Assert.Equal(ScheduleStatus.Paused, desc1.Status);

        //             await client.ResumeAsync();
        //             var desc2 = await client.DescribeAsync();
        //             Assert.Equal(ScheduleStatus.Active, desc2.Status);

        //             await Task.Delay(TimeSpan.FromSeconds(3));
        //         }
        //     }
        //     finally
        //     {
        //         await this.CleanupSchedule(scheduleId);
        //     }
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandleScheduleWithCustomData()
        // {
        //     var scheduleId = $"custom-data-{Guid.NewGuid()}";
        //     try
        //     {
        //         var customData = "{ \"key1\": \"value1\", \"key2\": 42 }";
        //         var client = await this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //             scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromMinutes(5))
        //         {
        //             OrchestrationInput = customData
        //         });

        //         var desc = await client.DescribeAsync();
        //         Assert.NotNull(desc.OrchestrationInput);
        //     }
        //     finally
        //     {
        //         await this.CleanupSchedule(scheduleId);
        //     }
        // }

        // [Fact]
        // public async Task Schedule_ShouldHandleParallelScheduleOperations()
        // {
        //     var baseScheduleId = $"parallel-ops-{Guid.NewGuid()}";
        //     var tasks = new List<Task>();

        //     try
        //     {
        //         // Create multiple schedules in parallel
        //         for (int i = 0; i < 5; i++)
        //         {
        //             var scheduleId = $"{baseScheduleId}-{i}";
        //             tasks.Add(this.ScheduledTaskClient.CreateScheduleAsync(new ScheduleCreationOptions(
        //                 scheduleId, nameof(SimpleOrchestrator), TimeSpan.FromMinutes(5))));
        //         }

        //         await Task.WhenAll(tasks);

        //         // Verify all schedules were created
        //         var schedules = this.ScheduledTaskClient.ListSchedulesAsync();
        //         int count = 0;
        //         await foreach (var schedule in schedules)
        //         {
        //             if (schedule.ScheduleId.StartsWith(baseScheduleId))
        //             {
        //                 count++;
        //             }
        //         }
        //         Assert.True(count >= 5);
        //     }
        //     finally
        //     {
        //         // Cleanup all created schedules
        //         for (int i = 0; i < 5; i++)
        //         {
        //             await this.CleanupSchedule($"{baseScheduleId}-{i}");
        //         }
        //     }
        // }

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
        AsyncPageable<OrchestrationMetadata> GetOrchInstances(string instanceidprefix)
        {
            return this.Client.GetAllInstancesAsync(new OrchestrationQuery()
            {
                CreatedFrom = DateTimeOffset.UtcNow.AddMinutes(-3),
                InstanceIdPrefix = instanceidprefix,
                FetchInputsAndOutputs = true
            });
        }

        // helper function get count from async paageable
        async Task<int> GetCountFromPageable(AsyncPageable<OrchestrationMetadata> pageable)
        {
            int count = 0;
            await foreach (var instance in pageable)
            {
                count++;
            }
            return count;
        }

        // get orchestrantion scheduled time from parsing instanceid
        DateTimeOffset GetOrchestrationScheduledTime(string instanceId)
        {
            var parts = instanceId.Split('-');
            return DateTimeOffset.Parse(parts[1]);
        }

        // get a list of orchestration scheduled times from a list of instance ids
        List<DateTimeOffset> GetOrchestrationScheduledTimes(List<string> instanceIds)
        {
            return instanceIds.Select(this.GetOrchestrationScheduledTime).ToList();
        }

        // function get instanceids from async pageable
        async Task<List<string>> GetInstanceIdsFromPageable(AsyncPageable<OrchestrationMetadata> pageable)
        {
            var instanceIds = new List<string>();
            await foreach (var instance in pageable)
            {
                instanceIds.Add(instance.InstanceId);       
            }
            return instanceIds;
        }   
    }
}