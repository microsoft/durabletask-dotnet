using Microsoft.DurableTask;
using System;
using System.Threading.Tasks;

namespace ScheduleTests.Tasks
{
    [DurableTask]
    public class SimpleOrchestrator
    {
        public async Task<string> RunAsync(TaskOrchestrationContext context, string input)
        {
            return await context.CallActivityAsync<string>(nameof(TestActivity), $"Simple-{input}");
        }
    }

    [DurableTask]
    public class LongRunningOrchestrator
    {
        public async Task<string> RunAsync(TaskOrchestrationContext context, string input)
        {
            return await context.CallActivityAsync<string>(nameof(TestActivity), $"LongRunning-{input}");
        }
    }

    [DurableTask]
    public class RandomRunTimeOrchestrator
    {
        public async Task<string> RunAsync(TaskOrchestrationContext context, string input)
        {
            return await context.CallActivityAsync<string>(nameof(TestActivity), $"RandomRunTime-{input}");
        }
    }

    // add always throw exception orchestrator
    [DurableTask]
    public class AlwaysThrowExceptionOrchestrator
    {
        public async Task<string> RunAsync(TaskOrchestrationContext context, string input)
        {
            throw new Exception("Always throw exception");
        }
    }
} 