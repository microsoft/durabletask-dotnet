// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;

namespace Microsoft.DurableTask.InProcessTestFramework;

/// <summary>
/// A simple "Hello World" orchestrator for demonstration and testing purposes.
/// </summary>
public class HelloWorldOrchestrator : TaskOrchestrator<string, string>
{
    /// <inheritdoc/>
    public override async Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        // Call an activity to say hello
        string greeting = await context.CallActivityAsync<string>("SayHello", input);
        
        // Call another activity to get the current time
        string timeInfo = await context.CallActivityAsync<string>("GetCurrentTime", null);
        
        // Combine the results
        return $"{greeting} at {timeInfo}";
    }
}

/// <summary>
/// A simple activity that says hello to the provided name.
/// </summary>
[DurableTask("SayHello")]
public class SayHelloActivity : TaskActivity<string?, string>
{
    /// <inheritdoc/>
    public override Task<string> RunAsync(TaskActivityContext context, string? name)
    {
        return Task.FromResult($"Hello, {name ?? "World"}!");
    }
}

/// <summary>
/// A simple activity that returns the current time.
/// </summary>
[DurableTask("GetCurrentTime")]
public class GetCurrentTimeActivity : TaskActivity<object?, string>
{
    /// <inheritdoc/>
    public override Task<string> RunAsync(TaskActivityContext context, object? input)
    {
        return Task.FromResult(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"));
    }
}
