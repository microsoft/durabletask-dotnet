using Microsoft.DurableTask;
using System;
using System.Threading.Tasks;

namespace ScheduleTests.Tasks
{
    public class TestActivity : TaskActivity<string, string>
    {
        public override async Task<string> RunAsync(TaskActivityContext context, string input)
        {
            await Task.Delay(100); // Small delay to simulate work
            return $"Completed activity with input: {input} at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
        }
    }
} 