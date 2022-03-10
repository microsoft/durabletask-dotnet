// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using DurableTask.Grpc;
using Xunit;
using Xunit.Abstractions;

namespace DurableTask.Sdk.Tests;

/// <summary>
/// Integration tests that are designed to exercise the error handling and retry functionality
/// of the Durable Task SDK.
/// </summary>
public class OrchestrationErrorHandling : IntegrationTestBase
{
    public OrchestrationErrorHandling(ITestOutputHelper output, GrpcSidecarFixture sidecarFixture)
        : base(output, sidecarFixture)
    { }

    /// <summary>
    /// Tests the behavior and output of an unhandled exception that originates from an activity.
    /// </summary>
    [Fact]
    public async Task UnhandledActivityException()
    {
        string errorMessage = "Kah-BOOOOOM!!!"; // Use an obviously fake error message to avoid confusion when debugging

        TaskName orchestratorName = "FaultyOrchestration";
        TaskName activityName = "FaultyActivity";

        // Use local function definitions to simplify the validation of the callstacks
        async Task MyOrchestrationImpl(TaskOrchestrationContext ctx) => await ctx.CallActivityAsync(activityName);
        void MyActivityImpl(TaskActivityContext ctx) => throw new Exception(errorMessage);

        await using DurableTaskGrpcWorker server = this.CreateWorkerBuilder()
            .AddTasks(tasks => tasks
                .AddOrchestrator(orchestratorName, MyOrchestrationImpl)
                .AddActivity(activityName, MyActivityImpl))
            .Build();
        await server.StartAsync(this.TimeoutToken);

        DurableTaskClient client = this.CreateDurableTaskClient();
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await client.WaitForInstanceCompletionAsync(
            instanceId,
            this.TimeoutToken,
            getInputsAndOutputs: true);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);

        Assert.NotNull(metadata.FailureDetails);
        OrchestrationFailureDetails failureDetails = metadata.FailureDetails!;
        Assert.Equal(typeof(TaskFailedException).FullName, failureDetails.ErrorName);

        // Expecting something like:
        //    "The activity 'FaultyActivity' (#0) failed with an unhandled exception: Kah-BOOOOOM!!!"
        int failingTaskId = 0; // This is the first task to be scheduled by the orchestrator, thus taskID = 0
        Assert.Contains($"#{failingTaskId}", failureDetails.ErrorMessage);
        Assert.Contains(activityName, failureDetails.ErrorMessage);
        Assert.Contains(errorMessage, failureDetails.ErrorMessage);

        // A callstack for the orchestration is expected in the error details (not the activity callstack).
        Assert.NotNull(failureDetails.ErrorDetails);
        Assert.Contains(nameof(MyOrchestrationImpl), failureDetails.ErrorDetails);
        Assert.DoesNotContain(nameof(MyActivityImpl), failureDetails.ErrorDetails);
    }

    /// <summary>
    /// Tests the behavior and output of an unhandled exception that occurs in orchestrator code.
    /// </summary>
    /// <remarks>
    /// This is different from <see cref="UnhandledActivityException"/> in that the source of the
    /// exception is in the orchestrator code directly, and not from an unhandled activity task.
    /// </remarks>
    [Fact]
    public async Task UnhandledOrchestratorException()
    {
        string errorMessage = "Kah-BOOOOOM!!!"; // Use an obviously fake error message to avoid confusion when debugging
        string? expectedCallstack = null;

        TaskName orchestratorName = "FaultyOrchestration";
        await using DurableTaskGrpcWorker server = this.CreateWorkerBuilder()
            .AddTasks(tasks =>
                tasks.AddOrchestrator(orchestratorName, ctx =>
                {
                    expectedCallstack = Environment.StackTrace;
                    throw new Exception(errorMessage);
                }))
            .Build();
        await server.StartAsync(this.TimeoutToken);

        DurableTaskClient client = this.CreateDurableTaskClient();
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await client.WaitForInstanceCompletionAsync(
            instanceId,
            this.TimeoutToken,
            getInputsAndOutputs: true);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);

        Assert.NotNull(metadata.FailureDetails);
        OrchestrationFailureDetails failureDetails = metadata.FailureDetails!;
        Assert.Equal(typeof(Exception).FullName, failureDetails.ErrorName);
        Assert.Equal(errorMessage, failureDetails.ErrorMessage);
        Assert.NotNull(failureDetails.ErrorDetails);
        Assert.NotNull(expectedCallstack);
        Assert.Contains(expectedCallstack![..300], failureDetails.ErrorDetails);
    }

    /////// <summary>
    /////// Tests retry policies for activity calls.
    /////// </summary>
    ////[Fact]
    ////public async Task RetryActivityFailures()
    ////{
    ////    throw new NotImplementedException();
    ////}

    /////// <summary>
    /////// Tests retry policies for sub-orchestrations.
    /////// </summary>
    ////[Fact]
    ////public async Task RetrySubOrchestrationFailures()
    ////{
    ////    throw new NotImplementedException();
    ////}
}
