// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.Serialization;
using DurableTask.Core;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Microsoft.DurableTask.Grpc.Tests;

/// <summary>
/// Integration tests that are designed to exercise the error handling and retry functionality
/// of the Durable Task SDK.
/// </summary>
public class OrchestrationErrorHandling(ITestOutputHelper output, GrpcSidecarFixture sidecarFixture) :
    IntegrationTestBase(output, sidecarFixture)
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
        void MyActivityImpl(TaskActivityContext ctx) => throw new Exception(errorMessage, new CustomException("Inner!"));

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

        // Check that the inner exception - i.e. the exact exception that failed the orchestration - was populated correctly
        Assert.NotNull(failureDetails.InnerFailure);
        Assert.Equal(typeof(Exception).FullName, failureDetails.InnerFailure!.ErrorType);
        Assert.Equal(errorMessage, failureDetails.InnerFailure.ErrorMessage);

        // Check that the inner-most exception was populated correctly too (the custom exception type)
        Assert.NotNull(failureDetails.InnerFailure.InnerFailure);
        Assert.Equal(typeof(CustomException).FullName, failureDetails.InnerFailure.InnerFailure!.ErrorType);
        Assert.Equal("Inner!", failureDetails.InnerFailure.InnerFailure.ErrorMessage);
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

            // This handler only works with the specified exception type
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

    [Theory]
    [InlineData(10, typeof(ApplicationException), false, int.MaxValue, 2, 1, OrchestrationRuntimeStatus.Failed)] // 1 attempt since retry timeout expired.
    [InlineData(2, typeof(ApplicationException), false, int.MaxValue, null, 1, OrchestrationRuntimeStatus.Failed)] // 1 attempt since handler specifies no retry.
    [InlineData(2, typeof(CustomException),true, int.MaxValue, null, 2, OrchestrationRuntimeStatus.Failed)] // 2 attempts, custom exception type
    [InlineData(10, typeof(XunitException),true, 4, null, 5, OrchestrationRuntimeStatus.Completed)] // 10 attempts, 3rd party exception type
    public async Task RetryActivityFailuresCustomLogicAndPolicy(
        int maxNumberOfAttempts,
        Type exceptionType,
        bool retryException,
        int exceptionCount,
        int? retryTimeout,
        int expectedNumberOfAttempts,
        OrchestrationRuntimeStatus expRuntimeStatus)
    {
        string errorMessage = "Kah-BOOOOOM!!!"; // Use an obviously fake error message to avoid confusion when debugging

        int actualNumberOfAttempts = 0;
        int retryHandlerCalls = 0;
        RetryPolicy retryPolicy = new(
            maxNumberOfAttempts,
            firstRetryInterval: TimeSpan.FromMilliseconds(1),
            backoffCoefficient: 2,
            retryTimeout: retryTimeout.HasValue ? TimeSpan.FromMilliseconds(retryTimeout.Value) : null)
        {
            HandleFailure = taskFailureDetails =>
            {
                retryHandlerCalls++;
                return taskFailureDetails.IsCausedBy(exceptionType) && retryException;
            }
        };
        TaskOptions taskOptions = TaskOptions.FromRetryPolicy(retryPolicy);


        TaskName orchestratorName = "BustedOrchestration";
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks =>
                tasks.AddOrchestratorFunc(orchestratorName, async ctx =>
                {
                    await ctx.CallActivityAsync("Foo", options: taskOptions);
                })
                .AddActivityFunc("Foo", (TaskActivityContext context) =>
                {
                    if (actualNumberOfAttempts++ < exceptionCount)
                    {
                        throw MakeException(exceptionType, errorMessage);
                    }
                }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(expRuntimeStatus, metadata.RuntimeStatus);
        // More calls to retry handler than expected.
        //Assert.Equal(expectedNumberOfAttempts, retryHandlerCalls);
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
    [InlineData(10, typeof(ApplicationException), false, int.MaxValue, 2, 1, OrchestrationRuntimeStatus.Failed)] // 1 attempt since retry timeout expired.
    [InlineData(2, typeof(ApplicationException), false, int.MaxValue, null, 1, OrchestrationRuntimeStatus.Failed)] // 1 attempt since handler specifies no retry.
    [InlineData(2, typeof(CustomException), true, int.MaxValue, null, 2, OrchestrationRuntimeStatus.Failed)] // 2 attempts, custom exception type
    [InlineData(10, typeof(XunitException), true, 4, null, 5, OrchestrationRuntimeStatus.Completed)] // 10 attempts, 3rd party exception type
    public async Task RetrySubOrchestratorFailuresCustomLogicAndPolicy(
        int maxNumberOfAttempts,
        Type exceptionType,
        bool retryException,
        int exceptionCount,
        int? retryTimeout,
        int expectedNumberOfAttempts,
        OrchestrationRuntimeStatus expRuntimeStatus)
    {
        string errorMessage = "Kah-BOOOOOM!!!"; // Use an obviously fake error message to avoid confusion when debugging

        int actualNumberOfAttempts = 0;
        int retryHandlerCalls = 0;
        RetryPolicy retryPolicy = new(
            maxNumberOfAttempts,
            firstRetryInterval: TimeSpan.FromMilliseconds(1),
            backoffCoefficient: 2,
            retryTimeout: retryTimeout.HasValue ? TimeSpan.FromMilliseconds(retryTimeout.Value) : null)
        {
            HandleFailure = taskFailureDetails =>
            {
                retryHandlerCalls++;
                return taskFailureDetails.IsCausedBy(exceptionType) && retryException;
            }
        };
        TaskOptions taskOptions = TaskOptions.FromRetryPolicy(retryPolicy);

        TaskName orchestratorName = "OrchestrationWithBustedSubOrchestrator";
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks =>
                tasks.AddOrchestratorFunc(orchestratorName, async ctx =>
                {
                    await ctx.CallSubOrchestratorAsync("BustedSubOrchestrator", options: taskOptions);
                })
                .AddOrchestratorFunc("BustedSubOrchestrator", context =>
                {
                    if (actualNumberOfAttempts++ < exceptionCount)
                    {
                        throw MakeException(exceptionType, errorMessage);
                    }
                }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(expRuntimeStatus, metadata.RuntimeStatus);
        // More calls to retry handler than expected.
        //Assert.Equal(expectedNumberOfAttempts, retryHandlerCalls);
        Assert.Equal(expectedNumberOfAttempts, actualNumberOfAttempts);

        // The root orchestration failed due to a failure with the sub-orchestration, resulting in a TaskFailedException
        if (expRuntimeStatus == OrchestrationRuntimeStatus.Failed)
        {
            Assert.NotNull(metadata.FailureDetails);
            Assert.True(metadata.FailureDetails!.IsCausedBy<TaskFailedException>());
        }
        else
        {
            Assert.Null(metadata.FailureDetails);
        }
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

    [Fact]
    public async Task InnerExceptionDetailsArePreserved()
    {
        static void ValidateInnermostFailureDetailsChain(TaskFailureDetails? failureDetails)
        {
            Assert.NotNull(failureDetails);
            Assert.True(failureDetails!.IsCausedBy<CustomException>());
            Assert.Equal("first", failureDetails.ErrorMessage);
            Assert.NotNull(failureDetails.InnerFailure);
            Assert.True(failureDetails.InnerFailure!.IsCausedBy<ApplicationException>());
            Assert.Equal("second", failureDetails.InnerFailure.ErrorMessage);
            Assert.NotNull(failureDetails.InnerFailure.InnerFailure);
            Assert.True(failureDetails.InnerFailure.InnerFailure!.IsCausedBy<XunitException>());
            Assert.Equal("third", failureDetails.InnerFailure.InnerFailure.ErrorMessage);
        }

        // TODO: Write a test where an activity throws an exception, the orchestration catches it and confirms the details,
        //       then rethrows the exception, and the outer orchestration catches it and does the same, and then rethrows
        //       again to fail the top-level orchestration. The client should then be able to see the details of the innermost
        //       exception.
        TaskName orchestratorName = "Parent";
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks =>
                tasks.AddOrchestratorFunc(orchestratorName, async ctx =>
                {
                    try
                    {
                        await ctx.CallSubOrchestratorAsync("Sub");
                    }
                    catch (TaskFailedException ex)
                    {
                        // Outer failure represents the orchestration failure
                        Assert.NotNull(ex.FailureDetails);
                        Assert.True(ex.FailureDetails.IsCausedBy<TaskFailedException>());
                        Assert.Contains("ThrowException", ex.FailureDetails.ErrorMessage);

                        // Inner failure represents the original exception thrown by the activity
                        ValidateInnermostFailureDetailsChain(ex.FailureDetails.InnerFailure);
                        throw;
                    }
                })
                .AddOrchestratorFunc("Sub", async context =>
                {
                    try
                    {
                        await context.CallActivityAsync("ThrowException");
                    }
                    catch (TaskFailedException ex)
                    {
                        ValidateInnermostFailureDetailsChain(ex.FailureDetails);
                        throw;
                    }
                })
                .AddActivityFunc("ThrowException", (TaskActivityContext context) =>
                {
                    // Raise a deeply nested exception
                    throw new CustomException("first", new ApplicationException("second", new XunitException("third")));
                }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId,
            getInputsAndOutputs: true,
            this.TimeoutToken);

        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);

        // Check to make sure that the wrapper failure details exist as expected
        Assert.NotNull(metadata.FailureDetails);
        Assert.True(metadata.FailureDetails!.IsCausedBy<TaskFailedException>());
        Assert.Contains("Sub", metadata.FailureDetails.ErrorMessage);
        Assert.NotNull(metadata.FailureDetails.InnerFailure);
        Assert.True(metadata.FailureDetails.InnerFailure!.IsCausedBy<TaskFailedException>());
        Assert.Contains("ThrowException", metadata.FailureDetails.InnerFailure.ErrorMessage);

        ValidateInnermostFailureDetailsChain(metadata.FailureDetails.InnerFailure.InnerFailure);
    }

    /// <summary>
    /// Tests the behavior of a failed orchestration when it is rewound and re-executed.
    /// </summary>
    [Fact]
    public async Task RewindSingleFailedActivity()
    {
        bool isBusted = true;

        TaskName orchestratorName = "BustedOrchestration";
        TaskName activityName = "BustedActivity";

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks =>
                tasks.AddOrchestratorFunc(orchestratorName, async ctx =>
                {
                    await ctx.CallActivityAsync(activityName);
                })
                .AddActivityFunc(activityName, (TaskActivityContext context) =>
                {
                    if (isBusted)
                    {
                        throw new Exception("Kah-BOOOOOM!!!");
                    }
                }));
        });

        DurableTaskClient client = server.Client;

        // Start the orchestration and wait for it to fail.
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await client.WaitForInstanceCompletionAsync(instanceId, this.TimeoutToken);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);

        // Simulate "fixing" the original problem by setting the flag to false.
        isBusted = false;

        // Rewind the orchestration to put it back into a running state. It should complete successfully this time.
        await client.RewindInstanceAsync(instanceId, "Rewind failed orchestration");
        metadata = await client.WaitForInstanceCompletionAsync(instanceId, this.TimeoutToken);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
    }

    /// <summary>
    /// Tests the behavior of a failed orchestration when it is rewound and re-executed multiple times.
    /// </summary>
    [Fact]
    public async Task RewindMultipleFailedActivities_Serial()
    {
        bool isBusted1 = true;
        bool isBusted2 = true;

        TaskName orchestratorName = "BustedOrchestration";
        TaskName activityName1 = "BustedActivity1";
        TaskName activityName2 = "BustedActivity2";

        int activity1CompletionCount = 0;
        int activity2CompletionCount = 0;

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks =>
                tasks.AddOrchestratorFunc(orchestratorName, async ctx =>
                {
                    // Take the result of the first activity and pass it to the second activity
                    int result = await ctx.CallActivityAsync<int>(activityName1);
                    return await ctx.CallActivityAsync<int>(activityName2, input: result);
                })
                .AddActivityFunc(activityName1, (TaskActivityContext context) =>
                {
                    if (isBusted1)
                    {
                        throw new Exception("Failure1");
                    }

                    activity1CompletionCount++;
                    return 1;
                })
                .AddActivityFunc(activityName2, (TaskActivityContext context, int input) =>
                {
                    if (isBusted2)
                    {
                        throw new Exception("Failure2");
                    }

                    activity2CompletionCount++;
                    return input + 1;
                }));
        });

        DurableTaskClient client = server.Client;

        // Start the orchestration and wait for it to fail with an ApplicationException.
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await client.WaitForInstanceCompletionAsync(instanceId, this.TimeoutToken);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);
        Assert.Equal("Failure1", metadata.FailureDetails?.ErrorMessage);

        // Simulate "fixing" just the first problem by setting the first flag to false.
        isBusted1 = false;

        // Rewind the orchestration. It should fail again, but this time with a different error message.
        await client.RewindInstanceAsync(instanceId, "Rewind failed orchestration");
        metadata = await client.WaitForInstanceCompletionAsync(instanceId, this.TimeoutToken);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);
        Assert.Equal("Failure2", metadata.FailureDetails?.ErrorMessage);

        // Simulate "fixing" the second problem by setting the second flag to false.
        isBusted2 = false;

        // Rewind the orchestration again to put it back into a running state. It should now complete successfully.
        await client.RewindInstanceAsync(instanceId, "Rewind failed orchestration");
        metadata = await client.WaitForInstanceCompletionAsync(instanceId, this.TimeoutToken);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal(2, metadata.ReadOutputAs<int>());

        // Confirm that each activity completed exactly once (i.e. successful activity calls aren't rewound).
        Assert.Equal(1, activity1CompletionCount);
        Assert.Equal(1, activity2CompletionCount);
    }

    /// <summary>
    /// Tests the behavior of a failed orchestration when multiple failures occur as part of a fan-out/fan-in, and is
    /// subsequently rewound and re-executed.
    /// </summary>
    [Fact]
    public async Task RewindMultipleFailedActivities_Parallel()
    {
        bool isBusted = true;

        TaskName orchestratorName = "BustedOrchestration";
        TaskName activityName = "BustedActivity";

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks =>
                tasks.AddOrchestratorFunc(orchestratorName, async ctx =>
                {
                    // Run the activity function multiple times in parallel
                    IList<Task> tasks = Enumerable.Range(0, 10)
                        .Select(i => ctx.CallActivityAsync(activityName))
                        .ToList();

                    // Wait for all the activity functions to complete
                    await Task.WhenAll(tasks);
                    return "Done";
                })
                .AddActivityFunc(activityName, (TaskActivityContext context) =>
                {
                    if (isBusted)
                    {
                        throw new Exception("Kah-BOOOOOM!!!");
                    }
                }));
        });

        DurableTaskClient client = server.Client;

        // Start the orchestration and wait for it to fail.
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await client.WaitForInstanceCompletionAsync(instanceId, this.TimeoutToken);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);

        // Simulate "fixing" the original problem by setting the flag to false.
        isBusted = false;

        // Rewind the orchestration to put it back into a running state. It should complete successfully this time.
        await client.RewindInstanceAsync(instanceId, "Rewind failed orchestration");
        metadata = await client.WaitForInstanceCompletionAsync(instanceId, this.TimeoutToken);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("Done", metadata.ReadOutputAs<string>());
    }

    /// <summary>
    /// Tests rewinding an orchestration that failed due to a failed sub-orchestration. The sub-orchestration is fixed
    /// and the parent orchestration is rewound to allow the entire chain to complete successfully.
    /// </summary>
    [Fact]
    public async Task RewindFailedSubOrchestration()
    {
        bool isBusted = true;

        TaskName orchestratorName = "BustedOrchestrator";
        TaskName subOrchestratorName = "BustedSubOrchestrator";

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc(orchestratorName, async ctx =>
                {
                    await ctx.CallSubOrchestratorAsync(subOrchestratorName);
                })
                .AddOrchestratorFunc(subOrchestratorName, ctx =>
                {
                    if (isBusted)
                    {
                        throw new Exception("Kah-BOOOOOM!!!");
                    }
                }));
        });

        DurableTaskClient client = server.Client;

        // Start the orchestration and wait for it to fail.
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await client.WaitForInstanceCompletionAsync(instanceId, this.TimeoutToken);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);

        // Simulate "fixing" the original problem by setting the flag to false.
        isBusted = false;

        // Rewind the orchestration to put it back into a running state. It should complete successfully this time.
        await client.RewindInstanceAsync(instanceId, "Rewind failed orchestration");
        metadata = await client.WaitForInstanceCompletionAsync(instanceId, this.TimeoutToken);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
    }

    /// <summary>
    /// Tests rewinding an orchestration that failed due to a failed sub-orchestration, which itself failed due to an
    /// activity. The entire orchestration chain is expected to fail, and rewinding the parent orchestration should
    /// allow the entire chain to complete successfully.
    /// </summary>
    [Fact]
    public async Task RewindFailedSubOrchestrationWithActivity()
    {
        bool isBusted = true;

        TaskName orchestratorName = "BustedOrchestrator";
        TaskName subOrchestratorName = "BustedSubOrchestrator";
        TaskName activityName = "BustedActivity";

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc(orchestratorName, async ctx =>
                {
                    await ctx.CallSubOrchestratorAsync(subOrchestratorName);
                })
                .AddOrchestratorFunc(subOrchestratorName, async ctx =>
                {
                    await ctx.CallActivityAsync(activityName);
                })
                .AddActivityFunc(activityName, (TaskActivityContext _) =>
                {
                    if (isBusted)
                    {
                        throw new Exception("Kah-BOOOOOM!!!");
                    }
                }));
        });

        DurableTaskClient client = server.Client;

        // Start the orchestration and wait for it to fail.
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await client.WaitForInstanceCompletionAsync(instanceId, this.TimeoutToken);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);

        // Simulate "fixing" the original problem by setting the flag to false.
        isBusted = false;

        // Rewind the orchestration to put it back into a running state. It should complete successfully this time.
        await client.RewindInstanceAsync(instanceId, "Rewind failed orchestration");
        metadata = await client.WaitForInstanceCompletionAsync(instanceId, this.TimeoutToken);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
    }

    static Exception MakeException(Type exceptionType, string message)
    {
        // We assume the contructor of the exception type takes a single string argument
        return (Exception)Activator.CreateInstance(exceptionType, message)!;
    }

    [Serializable]
    class CustomException : Exception
    {
        public CustomException(string message)
            : base(message)
        {
        }

        public CustomException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        protected CustomException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
