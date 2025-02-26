// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;

namespace ScheduleTests.Tasks
{
    public class TestActivity : TaskActivity<string, string>
    {
        public override async Task<string> RunAsync(TaskActivityContext context, string input)
        {
            // if input starts with different strings
            if (input.StartsWith("Simple-")) {
                await Task.Delay(100); // Small delay to simulate work
            } else if (input.StartsWith("LongRunning-")) {
                await Task.Delay(1000); // Small delay to simulate work
            } else if (input.StartsWith("RandomRunTime-")) {
                await Task.Delay(new Random().Next(100, 1000)); // Small delay to simulate work
            }
            return $"Completed activity with input: {input} at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
        }
    }
}