// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Testing;
using Microsoft.DurableTask.Worker;
using Xunit;

namespace InProcessTestHost.Tests;

/// <summary>
/// Tests to verify InProcessTestHost works with both class-syntax and function-syntax orchestrations.
/// </summary>
public class BasicOrchestrationTests
{
    [Fact]
    // Test basic class-syntax orchestration with DurableTaskTestHost.
    public async Task TestClassSyntaxOrchestration()
    {
        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(tasks =>
        {
            tasks.AddOrchestrator<MyClassOrchestrator>();
            tasks.AddActivity<MyClassActivity>();
        });

        var instanceId = await host.Client.ScheduleNewOrchestrationInstanceAsync(nameof(MyClassOrchestrator), "Alice");
        var metadata = await host.Client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: true);


        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        var result = metadata.ReadOutputAs<string>();
        Assert.Equal("Hello, Alice!", result);
    }

    [Fact]
    // Test basic class-syntax orchestration with DurableTaskTestHost.
    public async Task TestFunctionSyntaxOrchestration()
    {

        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(tasks =>
        {
            tasks.AddOrchestratorFunc("MyFuncOrchestrator", async (TaskOrchestrationContext context, string name) =>
            {
                var result = await context.CallActivityAsync<string>("MyFuncActivity", name);
                return result;
            });

            tasks.AddActivityFunc<string, string>("MyFuncActivity", (TaskActivityContext context, string name) =>
            {
                return $"Greetings, {name}!";
            });
        });

        var instanceId = await host.Client.ScheduleNewOrchestrationInstanceAsync("MyFuncOrchestrator", "Bob");
        var metadata = await host.Client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: true);

        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        var result = metadata.ReadOutputAs<string>();
        Assert.Equal("Greetings, Bob!", result);
    }

    /// <summary>
    /// Class-based orchestrator that calls an activity.
    /// </summary>
    class MyClassOrchestrator : TaskOrchestrator<string, string>
    {
        public override async Task<string> RunAsync(TaskOrchestrationContext context, string name)
        {
            var result = await context.CallActivityAsync<string>(nameof(MyClassActivity), name);
            return result;
        }
    }

    /// <summary>
    /// Class-based activity that returns a greeting.
    /// </summary>
     class MyClassActivity : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string name)
        {
            return Task.FromResult($"Hello, {name}!");
        }
    }
}

