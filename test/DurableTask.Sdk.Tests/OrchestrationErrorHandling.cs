﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Microsoft.DurableTask.Grpc;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.DurableTask.Tests;

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
        TaskFailureDetails failureDetails = metadata.FailureDetails!;
        Assert.Equal(typeof(TaskFailedException).FullName, failureDetails.ErrorType);

        // Expecting something like:
        //    "The activity 'FaultyActivity' (#0) failed with an unhandled exception: Kah-BOOOOOM!!!"
        int failingTaskId = 0; // This is the first task to be scheduled by the orchestrator, thus taskID = 0
        Assert.Contains($"#{failingTaskId}", failureDetails.ErrorMessage);
        Assert.Contains(activityName, failureDetails.ErrorMessage);
        Assert.Contains(errorMessage, failureDetails.ErrorMessage);

        // A callstack for the orchestration is expected (but not the activity callstack).
        Assert.NotNull(failureDetails.StackTrace);
        Assert.Contains(nameof(MyOrchestrationImpl), failureDetails.StackTrace);
        Assert.DoesNotContain(nameof(MyActivityImpl), failureDetails.StackTrace);
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
                    // The Environment.StackTrace and throw statements need to be on the same line
                    // to keep line numbers consistent between the expected stack trace and the actual stack trace.
                    // Also need to remove the top frame from Environment.StackTrace.
                    expectedCallstack = Environment.StackTrace.Replace("at System.Environment.get_StackTrace()", string.Empty).TrimStart(); throw new Exception(errorMessage);
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
        TaskFailureDetails failureDetails = metadata.FailureDetails!;
        Assert.Equal(typeof(Exception).FullName, failureDetails.ErrorType);
        Assert.Equal(errorMessage, failureDetails.ErrorMessage);
        Assert.NotNull(failureDetails.StackTrace);
        Assert.NotNull(expectedCallstack);
        Assert.Contains(expectedCallstack![..300], failureDetails.StackTrace);
    }

    /// <summary>
    /// Tests retry policies for activity calls.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    public async Task RetryActivityFailures(int expectedNumberOfAttempts)
    {
        string errorMessage = "Kah-BOOOOOM!!!"; // Use an obviously fake error message to avoid confusion when debugging

        TaskOptions retryOptions = TaskOptions.FromRetryPolicy(new RetryPolicy(
            expectedNumberOfAttempts,
            firstRetryInterval: TimeSpan.FromMilliseconds(1)));

        int actualNumberOfAttempts = 0;

        TaskName orchestratorName = "BustedOrchestration";
        await using DurableTaskGrpcWorker server = this.CreateWorkerBuilder()
            .AddTasks(tasks =>
                tasks.AddOrchestrator(orchestratorName, async ctx =>
                {
                    await ctx.CallActivityAsync("Foo", options: retryOptions);
                })
                .AddActivity("Foo", context =>
                {
                    actualNumberOfAttempts++;
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
        Assert.Equal(expectedNumberOfAttempts, actualNumberOfAttempts);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    public async Task RetryActivityFailuresCustomLogic(int expectedNumberOfAttempts)
    {
        string errorMessage = "Kah-BOOOOOM!!!"; // Use an obviously fake error message to avoid confusion when debugging

        int retryHandlerCalls = 0;
        TaskOptions retryOptions = TaskOptions.FromRetryHandler(retryContext =>
        {
            // This is technically orchestrator code that gets replayed, like everything else
            if (!retryContext.OrchestrationContext.IsReplaying)
            {
                retryHandlerCalls++;
            }

            // IsCausedBy is supposed to handle exception inheritance; fail if it doesn't
            if (!retryContext.LastFailure.IsCausedBy<Exception>())
            {
                return false;
            }

            // This handler only works with ApplicationException
            if (!retryContext.LastFailure.IsCausedBy<ApplicationException>())
            {
                return false;
            }

            // Quit after N attempts
            return retryContext.LastAttemptNumber < expectedNumberOfAttempts;
        });

        int actualNumberOfAttempts = 0;

        TaskName orchestratorName = "BustedOrchestration";
        await using DurableTaskGrpcWorker server = this.CreateWorkerBuilder()
            .AddTasks(tasks =>
                tasks.AddOrchestrator(orchestratorName, async ctx =>
                {
                    await ctx.CallActivityAsync("Foo", options: retryOptions);
                })
                .AddActivity("Foo", context =>
                {
                    actualNumberOfAttempts++;
                    throw new ApplicationException(errorMessage);
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
        Assert.Equal(expectedNumberOfAttempts, retryHandlerCalls);
        Assert.Equal(expectedNumberOfAttempts, actualNumberOfAttempts);
    }

    /// <summary>
    /// Tests retry policies for sub-orchestration calls.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    public async Task RetrySubOrchestrationFailures(int expectedNumberOfAttempts)
    {
        string errorMessage = "Kah-BOOOOOM!!!"; // Use an obviously fake error message to avoid confusion when debugging

        TaskOptions retryOptions = TaskOptions.FromRetryPolicy(new RetryPolicy(
            expectedNumberOfAttempts,
            firstRetryInterval: TimeSpan.FromMilliseconds(1)));

        int actualNumberOfAttempts = 0;

        TaskName orchestratorName = "OrchestrationWithBustedSubOrchestrator";
        await using DurableTaskGrpcWorker server = this.CreateWorkerBuilder()
            .AddTasks(tasks =>
                tasks.AddOrchestrator(orchestratorName, async ctx =>
                {
                    await ctx.CallSubOrchestratorAsync("BustedSubOrchestrator", options: retryOptions);
                })
                .AddOrchestrator("BustedSubOrchestrator", context =>
                {
                    actualNumberOfAttempts++;
                    throw new ApplicationException(errorMessage);
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
        Assert.Equal(expectedNumberOfAttempts, actualNumberOfAttempts);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    public async Task RetrySubOrchestratorFailuresCustomLogic(int expectedNumberOfAttempts)
    {
        string errorMessage = "Kah-BOOOOOM!!!"; // Use an obviously fake error message to avoid confusion when debugging

        int retryHandlerCalls = 0;
        TaskOptions retryOptions = TaskOptions.FromRetryHandler(retryContext =>
        {
            // This is technically orchestrator code that gets replayed, like everything else
            if (!retryContext.OrchestrationContext.IsReplaying)
            {
                retryHandlerCalls++;
            }

            // IsCausedBy is supposed to handle exception inheritance; fail if it doesn't
            if (!retryContext.LastFailure.IsCausedBy<Exception>())
            {
                return false;
            }

            // This handler only works with ApplicationException
            if (!retryContext.LastFailure.IsCausedBy<ApplicationException>())
            {
                return false;
            }

            // Quit after N attempts
            return retryContext.LastAttemptNumber < expectedNumberOfAttempts;
        });

        int actualNumberOfAttempts = 0;

        TaskName orchestratorName = "OrchestrationWithBustedSubOrchestrator";
        await using DurableTaskGrpcWorker server = this.CreateWorkerBuilder()
            .AddTasks(tasks =>
                tasks.AddOrchestrator(orchestratorName, async ctx =>
                {
                    await ctx.CallSubOrchestratorAsync("BustedSubOrchestrator", options: retryOptions);
                })
                .AddOrchestrator("BustedSubOrchestrator", context =>
                {
                    actualNumberOfAttempts++;
                    throw new ApplicationException(errorMessage);
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
        Assert.Equal(expectedNumberOfAttempts, retryHandlerCalls);
        Assert.Equal(expectedNumberOfAttempts, actualNumberOfAttempts);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TaskNotFoundErrorsAreNotRetried(bool activity)
    {
        int retryHandlerCalls = 0;
        TaskOptions retryOptions = TaskOptions.FromRetryHandler(retryContext =>
        {
            retryHandlerCalls++;
            return false;
        });

        TaskName orchestratorName = "OrchestrationWithMissingTask";
        await using DurableTaskGrpcWorker server = this.CreateWorkerBuilder()
            .AddTasks(tasks =>
                tasks.AddOrchestrator(orchestratorName, async ctx =>
                {
                    if (activity)
                    {
                        await ctx.CallActivityAsync("Bogus", options: retryOptions);
                    }
                    else
                    {
                        await ctx.CallSubOrchestratorAsync("Bogus", options: retryOptions);
                    }
                }))
            .Build();
        await server.StartAsync(this.TimeoutToken);

        DurableTaskClient client = this.CreateDurableTaskClient();
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await client.WaitForInstanceCompletionAsync(
            instanceId,
            this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);

        // The retry handler should never get called for a missing activity or sub-orchestrator exception
        Assert.Equal(0, retryHandlerCalls);
    }
}
