// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Testing;

namespace Microsoft.DurableTask.Tests.Samples;

/// <summary>
/// Sample about how to use DurableTaskTestHost to test class-based orchestrations and activities in-process.
/// </summary>
public class DurableTaskTestHostSamples
{
    /// <summary>
    /// Example: Test a orchestration with a single activity.
    /// </summary>
    public static async Task BasicOrchestrationTest()
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

        // Assert the results using your test framework of choice (xUnit, NUnit, etc.)
        // Assert.NotNull(result);
        // Assert.Equal(OrchestrationRuntimeStatus.Completed, result.RuntimeStatus);
        // Assert.Equal("Hello, World!", result.ReadOutputAs<string>());
    }

    /// <summary>
    /// Sample orchestrator that calls a greeting activity.
    /// </summary>
    [DurableTask("HelloOrchestrator")]
    public class HelloOrchestrator : TaskOrchestrator<string, string>
    {
        /// <summary>
        /// Runs the orchestration logic.
        /// </summary>
        /// <param name="context">The orchestration context.</param>
        /// <param name="name">The input name to greet.</param>
        /// <returns>A task that represents the orchestration execution.</returns>
        public override async Task<string> RunAsync(TaskOrchestrationContext context, string name)
        {
            string greeting = await context.CallActivityAsync<string>("SayHello", name);
            return greeting;
        }
    }

    /// <summary>
    /// Sample activity that returns a greeting message.
    /// </summary>
    [DurableTask("SayHello")]
    public class SayHelloActivity : TaskActivity<string, string>
    {
        /// <summary>
        /// Runs the activity logic.
        /// </summary>
        /// <param name="context">The activity context.</param>
        /// <param name="name">The input name to greet.</param>
        /// <returns>A task that represents the activity execution.</returns>
        public override Task<string> RunAsync(TaskActivityContext context, string name)
        {
            return Task.FromResult($"Hello, {name}!");
        }
    }
}
