// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;

namespace ConsoleAppMinimal;

[DurableTask("HelloSequence")]
public class HelloSequenceOrchestrator : TaskOrchestrator<string, IEnumerable<string>>
{
    public override async Task<IEnumerable<string>> RunAsync(TaskOrchestrationContext context, string input)
    {
        IEnumerable<string> greetings =
        [
            await context.CallActivityAsync<string>("SayHello", "Tokyo"),
            await context.CallActivityAsync<string>("SayHello", "London"),
            await context.CallActivityAsync<string>("SayHello", "Seattle"),
        ];

        return greetings;
    }
}

[DurableTask("SayHello")]
public class SayHelloActivity : TaskActivity<string, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, string city)
    {
        return Task.FromResult($"Hello {city}!");
    }
}
