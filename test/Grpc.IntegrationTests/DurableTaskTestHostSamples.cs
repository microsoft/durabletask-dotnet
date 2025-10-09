// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Testing;

namespace Microsoft.DurableTask.Tests.Examples;

/// <summary>
/// Sample about how to use DurableTaskTestHost to test class-based orchestrations and activities in-process.
/// </summary>
public class DurableTaskTestHostExamples
{
    /// <summary>
    /// Test a orchestration with a single activity.
    /// </summary>
    [Fact]
    public async Task BasicOrchestrationTest()
    {
        // Start the test host with your orchestrators and activities
        await using var testHost = await DurableTaskTestHost.StartAsync(tasks =>
        {
            tasks.AddOrchestrator<HelloOrchestrator>();
            tasks.AddActivity<SayHelloActivity>();
        });

        // Schedule the orchestration via client
        string instanceId = await testHost.Client.ScheduleNewOrchestrationInstanceAsync(
            "HelloOrchestrator",
            input: "World");

        // Wait for completion and verify results
        OrchestrationMetadata result = await testHost.Client.WaitForInstanceCompletionAsync(
            instanceId,
            getInputsAndOutputs: true);

        Assert.NotNull(result);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, result.RuntimeStatus);
        Assert.Equal("Hello, World!", result.ReadOutputAs<string>());
    }

    [DurableTask("HelloOrchestrator")]
    public class HelloOrchestrator : TaskOrchestrator<string, string>
    {
        public override async Task<string> RunAsync(TaskOrchestrationContext context, string name)
        {
            string greeting = await context.CallActivityAsync<string>("SayHello", name);
            return greeting;
        }
    }

    [DurableTask("SayHello")]
    public class SayHelloActivity : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string name)
        {
            return Task.FromResult($"Hello, {name}!");
        }
    }
}
