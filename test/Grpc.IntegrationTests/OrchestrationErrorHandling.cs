// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Microsoft.DurableTask.Grpc.Tests;

/// <summary>
/// Integration tests that are designed to exercise the error handling and retry functionality
/// of the Durable Task SDK.
/// </summary>
public class OrchestrationErrorHandling(ITestOutputHelper output, GrpcSidecarFixture sidecarFixture) : IntegrationTestBase(output, sidecarFixture)
{

    /// <summary>
    /// Tests the behavior and output of an unhandled exception that originates from an activity.
    /// </summary>
    [Fact]
    public async Task UnhandledActivityException()
    {
        string errorMessage = "Kah-BOOOOOM!!!"; // Use an obviously fake error message to avoid confusion when debugging

        TaskName orchestratorName = "FaultyOrchestration";
        TaskName activityName = "FaultyActivity";

        // Use local function definitions to simplify the validation of the call stacks
        async Task MyOrchestrationImpl(TaskOrchestrationContext ctx) => await ctx.CallActivityAsync(activityName);
        void MyActivityImpl(TaskActivityContext ctx) => throw new Exception(errorMessage);

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc(orchestratorName, MyOrchestrationImpl)
                .AddActivityFunc(activityName, MyActivityImpl));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

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

        // A callstack for the orchestration is expected (but not the activity call stack).
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
    [Theory]
    [InlineData(typeof(ApplicationException))] // built-in exception type
    [InlineData(typeof(CustomException))] // custom exception type
    [InlineData(typeof(XunitException))] // 3rd party exception type
    public async Task UnhandledOrchestratorException(Type exceptionType)
    {
        string errorMessage = "Kah-BOOOOOM!!!"; // Use an obviously fake error message to avoid confusion when debugging
        string? expectedCallStack = null;

        TaskName orchestratorName = "FaultyOrchestration";
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc(orchestratorName, ctx =>
            {
                // The Environment.StackTrace and throw statements need to be on the same line
                // to keep line numbers consistent between the expected stack trace and the actual stack trace.
                // Also need to remove the top frame from Environment.StackTrace.
                expectedCallStack = Environment.StackTrace.Replace("at System.Environment.get_StackTrace()", string.Empty).TrimStart(); throw MakeException(exceptionType, errorMessage)!;
            }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);

        Assert.NotNull(metadata.FailureDetails);
        TaskFailureDetails failureDetails = metadata.FailureDetails!;
        Assert.Equal(exceptionType.FullName, failureDetails.ErrorType);
        Assert.Equal(errorMessage, failureDetails.ErrorMessage);
        Assert.NotNull(failureDetails.StackTrace);
        Assert.NotNull(expectedCallStack);
        Assert.Contains(expectedCallStack![..300], failureDetails.StackTrace);
        Assert.True(failureDetails.IsCausedBy(exceptionType));
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
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks =>
                tasks.AddOrchestratorFunc(orchestratorName, async ctx =>
                {
                    await ctx.CallActivityAsync("Foo", options: retryOptions);
                })
                .AddActivityFunc("Foo", (TaskActivityContext context) =>
                {
                    actualNumberOfAttempts++;
                    throw new Exception(errorMessage);
                }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);
        Assert.Equal(expectedNumberOfAttempts, actualNumberOfAttempts);
    }

    [Theory]
    [InlineData(1, typeof(ApplicationException))] // 1 attempt, built-in exception type
    [InlineData(2, typeof(CustomException))] // 2 attempts, custom exception type
    [InlineData(10, typeof(XunitException))] // 10 attempts, 3rd party exception type
    public async Task RetryActivityFailuresCustomLogic(int expectedNumberOfAttempts, Type exceptionType)
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

            // This handler only works with CustomException
            if (!retryContext.LastFailure.IsCausedBy(exceptionType))
            {
                return false;
            }

            // Quit after N attempts
            return retryContext.LastAttemptNumber < expectedNumberOfAttempts;
        });

        int actualNumberOfAttempts = 0;

        TaskName orchestratorName = "BustedOrchestration";
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks =>
                tasks.AddOrchestratorFunc(orchestratorName, async ctx =>
                {
                    await ctx.CallActivityAsync("Foo", options: retryOptions);
                })
                .AddActivityFunc("Foo", (TaskActivityContext context) =>
                {
                    actualNumberOfAttempts++;
                    throw MakeException(exceptionType, errorMessage);
                }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

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
    [InlineData(1, typeof(ApplicationException))] // 1 attempt, built-in exception type
    [InlineData(2, typeof(CustomException))] // 2 attempts, custom exception type
    [InlineData(10, typeof(XunitException))] // 10 attempts, 3rd party exception type
    public async Task RetrySubOrchestrationFailures(int expectedNumberOfAttempts, Type exceptionType)
    {
        string errorMessage = "Kah-BOOOOOM!!!"; // Use an obviously fake error message to avoid confusion when debugging

        TaskOptions retryOptions = TaskOptions.FromRetryPolicy(new RetryPolicy(
            expectedNumberOfAttempts,
            firstRetryInterval: TimeSpan.FromMilliseconds(1)));

        int actualNumberOfAttempts = 0;

        TaskName orchestratorName = "OrchestrationWithBustedSubOrchestrator";
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks =>
                tasks.AddOrchestratorFunc(orchestratorName, async ctx =>
                {
                    await ctx.CallSubOrchestratorAsync("BustedSubOrchestrator", options: retryOptions);
                })
                .AddOrchestratorFunc("BustedSubOrchestrator", context =>
                {
                    actualNumberOfAttempts++;
                    throw MakeException(exceptionType, errorMessage);
                }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);
        Assert.Equal(expectedNumberOfAttempts, actualNumberOfAttempts);
        Assert.NotNull(metadata.FailureDetails);
        Assert.Contains(errorMessage, metadata.FailureDetails!.ErrorMessage);

        // The root orchestration failed due to a failure with the sub-orchestration, resulting in a TaskFailedException
        Assert.True(metadata.FailureDetails.IsCausedBy<TaskFailedException>());
    }

    [Theory]
    [InlineData(1, typeof(ApplicationException))] // 1 attempt, built-in exception type
    [InlineData(2, typeof(CustomException))] // 2 attempts, custom exception type
    [InlineData(10, typeof(XunitException))] // 10 attempts, 3rd party exception type
    public async Task RetrySubOrchestratorFailuresCustomLogic(int expectedNumberOfAttempts, Type exceptionType)
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

            // This handler only works with CustomException
            if (!retryContext.LastFailure.IsCausedBy(exceptionType))
            {
                return false;
            }

            // Quit after N attempts
            return retryContext.LastAttemptNumber < expectedNumberOfAttempts;
        });

        int actualNumberOfAttempts = 0;

        TaskName orchestratorName = "OrchestrationWithBustedSubOrchestrator";
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks =>
                tasks.AddOrchestratorFunc(orchestratorName, async ctx =>
                {
                    await ctx.CallSubOrchestratorAsync("BustedSubOrchestrator", options: retryOptions);
                })
                .AddOrchestratorFunc("BustedSubOrchestrator", context =>
                {
                    actualNumberOfAttempts++;
                    throw MakeException(exceptionType, errorMessage);
                }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);
        Assert.Equal(expectedNumberOfAttempts, retryHandlerCalls);
        Assert.Equal(expectedNumberOfAttempts, actualNumberOfAttempts);

        // The root orchestration failed due to a failure with the sub-orchestration, resulting in a TaskFailedException
        Assert.NotNull(metadata.FailureDetails);
        Assert.True(metadata.FailureDetails!.IsCausedBy<TaskFailedException>());
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
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc(orchestratorName, async ctx =>
            {
                if (activity)
                {
                    await ctx.CallActivityAsync("Bogus", options: retryOptions);
                }
                else
                {
                    await ctx.CallSubOrchestratorAsync("Bogus", options: retryOptions);
                }
            }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId,
            this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);

        // The retry handler should never get called for a missing activity or sub-orchestrator exception
        Assert.Equal(0, retryHandlerCalls);
    }

    static Exception MakeException(Type exceptionType, string message)
    {
        // We assume the contructor of the exception type takes a single string argument
        return (Exception)Activator.CreateInstance(exceptionType, message)!;
    }

    class CustomException(string message) : Exception(message)
    {
    }
}
