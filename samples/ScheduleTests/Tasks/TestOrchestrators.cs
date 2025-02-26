using Microsoft.DurableTask;
using System;
using System.Threading.Tasks;

namespace ScheduleTests.Tasks
{
    [DurableTask]
    public class SimpleOrchestrator : ITaskOrchestrator
    {
        public Type InputType => typeof(string);
        public Type OutputType => typeof(string);

        public Task<string> RunAsync(TaskOrchestrationContext context, string input)
        {
            return context.CallActivityAsync<string>(nameof(TestActivity), $"Simple-{input}");
        }

        Task<object?> ITaskOrchestrator.RunAsync(TaskOrchestrationContext context, object? input)
        {
            return RunAsync(context, input?.ToString() ?? string.Empty).ContinueWith(t => (object?)t.Result);
        }
    }

    [DurableTask]
    public class LongRunningOrchestrator : ITaskOrchestrator
    {
        public Type InputType => typeof(string);
        public Type OutputType => typeof(string);

        public Task<string> RunAsync(TaskOrchestrationContext context, string input)
        {
            return context.CallActivityAsync<string>(nameof(TestActivity), $"LongRunning-{input}");
        }

        Task<object?> ITaskOrchestrator.RunAsync(TaskOrchestrationContext context, object? input)
        {
            return RunAsync(context, input?.ToString() ?? string.Empty).ContinueWith(t => (object?)t.Result);
        }
    }

    [DurableTask]
    public class RandomRunTimeOrchestrator : ITaskOrchestrator
    {
        public Type InputType => typeof(string);
        public Type OutputType => typeof(string);

        public Task<string> RunAsync(TaskOrchestrationContext context, string input)
        {
            return context.CallActivityAsync<string>(nameof(TestActivity), $"RandomRunTime-{input}");
        }

        Task<object?> ITaskOrchestrator.RunAsync(TaskOrchestrationContext context, object? input)
        {
            return RunAsync(context, input?.ToString() ?? string.Empty).ContinueWith(t => (object?)t.Result);
        }
    }

    // add always throw exception orchestrator
    [DurableTask]
    public class AlwaysThrowExceptionOrchestrator : ITaskOrchestrator
    {
        public Type InputType => typeof(string);
        public Type OutputType => typeof(string);

        public Task<string> RunAsync(TaskOrchestrationContext context, string input)
        {
            throw new Exception("Always throw exception");
        }

        Task<object?> ITaskOrchestrator.RunAsync(TaskOrchestrationContext context, object? input)
        {
            return RunAsync(context, input?.ToString() ?? string.Empty).ContinueWith(t => (object?)t.Result);
        }
    }
} 