// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Xunit;

namespace Microsoft.DurableTask.InProcessTestFramework;

/// <summary>
/// Simple test example showing how to test orchestrations with the in-process framework.
/// </summary>
public class SimpleTest
{
    [Fact]
    public async Task TestHelloWorldOrchestration_WithRealActivities()
    {
        // 1. Create the test framework
        using var framework = new InProcessTestFramework();
        
        // 2. Register your orchestration and activities (your existing code, unchanged)
        framework
            .RegisterOrchestrator("HelloWorld", new HelloWorldOrchestrator())
            .RegisterActivity("SayHello", new SayHelloActivity())
            .RegisterActivity("GetCurrentTime", new GetCurrentTimeActivity());

        // 3. Schedule the orchestration using the mock client (just like real client)
        string instanceId = await framework.Client.ScheduleNewOrchestrationInstanceAsync(
            "HelloWorld", 
            "Alice");

        // 4. Wait for it to finish
        var result = await framework.Client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: true);

        // 5. Check the output is what you want
        Assert.Equal(OrchestrationRuntimeStatus.Completed, result.RuntimeStatus);
        
        // The output should contain "Hello, Alice!" from SayHello activity
        Assert.Contains("Hello, Alice!", result.SerializedOutput ?? "");
        
        // The output should contain "UTC" from GetCurrentTime activity  
        Assert.Contains("UTC", result.SerializedOutput ?? "");
        
        // The format should be "Hello, Alice! at [timestamp] UTC"
        Assert.Matches(@"Hello, Alice! at .+ UTC", result.SerializedOutput ?? "");
    }

    [Fact]
    public async Task TestHelloWorldOrchestration_WithDifferentInput()
    {
        using var framework = new InProcessTestFramework();
        
        // Register the same orchestration and activities
        framework
            .RegisterOrchestrator("HelloWorld", new HelloWorldOrchestrator())
            .RegisterActivity("SayHello", new SayHelloActivity())
            .RegisterActivity("GetCurrentTime", new GetCurrentTimeActivity());

        // Test with different input
        string instanceId = await framework.Client.ScheduleNewOrchestrationInstanceAsync(
            "HelloWorld", 
            "Bob");

        var result = await framework.Client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: true);

        // Verify the orchestration ran successfully
        Assert.Equal(OrchestrationRuntimeStatus.Completed, result.RuntimeStatus);
        
        // Verify the output reflects the different input
        Assert.Contains("Hello, Bob!", result.SerializedOutput ?? "");
        Assert.Contains("UTC", result.SerializedOutput ?? "");
    }
}
