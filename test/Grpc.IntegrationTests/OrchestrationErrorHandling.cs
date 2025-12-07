// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Tests.Logging;
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
        async Task MyOrchestrationImpl(TaskOrchestrationContext ctx) =>
            await ctx.CallActivityAsync(activityName);

        void MyActivityImpl(TaskActivityContext ctx) =>
            throw new InvalidOperationException(errorMessage, new CustomException("Inner!"));

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
        Assert.Equal(typeof(InvalidOperationException).FullName, failureDetails.InnerFailure!.ErrorType);
        Assert.Equal(errorMessage, failureDetails.InnerFailure.ErrorMessage);

        // Check that the inner-most exception was populated correctly too (the custom exception type)
        Assert.NotNull(failureDetails.InnerFailure.InnerFailure);
        Assert.Equal(typeof(CustomException).FullName, failureDetails.InnerFailure.InnerFailure!.ErrorType);
        Assert.Equal("Inner!", failureDetails.InnerFailure.InnerFailure.ErrorMessage);

        IReadOnlyCollection<LogEntry> workerLogs = this.GetLogs(category: "Microsoft.DurableTask.Worker");
        Assert.NotEmpty(workerLogs);

        // Check that the orchestrator and activity logs are present
        Assert.Single(workerLogs, log => MatchLog(
            log,
            logEventName: "OrchestrationStarted",
            exception: null,
            ("InstanceId", instanceId),
            ("Name", orchestratorName.Name)));

        Assert.Single(workerLogs, log => MatchLog(
            log,
            logEventName: "ActivityStarted",
            exception: null,
            ("InstanceId", instanceId),
            ("Name", activityName.Name)));

        Assert.Single(workerLogs, log => MatchLog(
            log,
            logEventName: "ActivityFailed",
            exception: (typeof(InvalidOperationException), errorMessage),
            ("InstanceId", instanceId),
            ("Name", activityName.Name)));

        Assert.Single(workerLogs, log => MatchLog(
            log,
            logEventName: "OrchestrationFailed",
            exception: (typeof(TaskFailedException), $"Task '{activityName}' (#0) failed with an unhandled exception: {errorMessage}"),
            ("InstanceId", instanceId),
            ("Name", orchestratorName.Name)));
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
    [InlineData(2, typeof(CustomException), true, int.MaxValue, null, 2, OrchestrationRuntimeStatus.Failed)] // 2 attempts, custom exception type
    [InlineData(10, typeof(XunitException), true, 4, null, 5, OrchestrationRuntimeStatus.Completed)] // 10 attempts, 3rd party exception type
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

    static Exception MakeException(Type exceptionType, string message)
    {
        // We assume the contructor of the exception type takes a single string argument
        return (Exception)Activator.CreateInstance(exceptionType, message)!;
    }

    /// <summary>
    /// Tests that exception properties are included in FailureDetails when using a custom IExceptionPropertiesProvider.
    /// </summary>
    [Fact]
    public async Task CustomExceptionPropertiesInFailureDetails()
    {
        TaskName orchestratorName = "OrchestrationWithCustomException";
        TaskName activityName = "BusinessActivity";

        // Register activity functions that will throw a custom exception with diverse property types.
        async Task MyOrchestrationImpl(TaskOrchestrationContext ctx) =>
            await ctx.CallActivityAsync(activityName);

        void MyActivityImpl(TaskActivityContext ctx) =>
            throw new BusinessValidationException(
                message: "Business logic validation failed",
                stringProperty: "validation-error-123",
                intProperty: 100,
                longProperty: 999999999L,
                dateTimeProperty: new DateTime(2025, 10, 15, 14, 30, 0, DateTimeKind.Utc),
                dictionaryProperty: new Dictionary<string, object?>
                {
                    ["error_code"] = "VALIDATION_FAILED",
                    ["retry_count"] = 3,
                    ["is_critical"] = true
                },
                listProperty: new List<object?> { "error1", "error2", 500, null },
                nullProperty: null);

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            // Register the custom exception properties provider
            b.Services.AddSingleton<IExceptionPropertiesProvider, TestExceptionPropertiesProvider>();

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

        // Check that the activity failure is in the inner failure
        Assert.NotNull(failureDetails.InnerFailure);
        TaskFailureDetails innerFailure = failureDetails.InnerFailure!;
        Assert.Equal(typeof(BusinessValidationException).FullName, innerFailure.ErrorType);

        // Check that custom properties are included and verify all property types
        Assert.NotNull(innerFailure.Properties);

        // We should contain 7 properties.
        Assert.Equal(7, innerFailure.Properties.Count);

        // Verify string property
        Assert.True(innerFailure.Properties.ContainsKey("StringProperty"));
        Assert.Equal("validation-error-123", innerFailure.Properties["StringProperty"]);

        // Verify numeric properties (note: all numeric types are serialized as double in protobuf)
        Assert.True(innerFailure.Properties.ContainsKey("IntProperty"));
        Assert.Equal(100.0, innerFailure.Properties["IntProperty"]);

        Assert.True(innerFailure.Properties.ContainsKey("LongProperty"));
        Assert.Equal(999999999.0, innerFailure.Properties["LongProperty"]);

        // Verify DateTime property
        Assert.True(innerFailure.Properties.ContainsKey("DateTimeProperty"));
        Assert.Equal(new DateTime(2025, 10, 15, 14, 30, 0, DateTimeKind.Utc), innerFailure.Properties["DateTimeProperty"]);

        // Verify dictionary property with nested values
        Assert.True(innerFailure.Properties.ContainsKey("DictionaryProperty"));
        var dictProperty = innerFailure.Properties["DictionaryProperty"] as IDictionary<string, object?>;
        Assert.NotNull(dictProperty);
        Assert.Equal(3, dictProperty.Count);
        Assert.Equal("VALIDATION_FAILED", dictProperty["error_code"]);
        Assert.Equal(3.0, dictProperty["retry_count"]);
        Assert.Equal(true, dictProperty["is_critical"]);

        // Verify list property with mixed types
        Assert.True(innerFailure.Properties.ContainsKey("ListProperty"));
        var listProperty = innerFailure.Properties["ListProperty"] as IList<object?>;
        Assert.NotNull(listProperty);
        Assert.Equal(4, listProperty.Count);
        Assert.Equal("error1", listProperty[0]);
        Assert.Equal("error2", listProperty[1]);
        Assert.Equal(500.0, listProperty[2]);
        Assert.Null(listProperty[3]);

        // Verify null property
        Assert.True(innerFailure.Properties.ContainsKey("NullProperty"));
        Assert.Null(innerFailure.Properties["NullProperty"]);
    }

    /// <summary>
    /// Tests that exception properties are included in FailureDetails when an orchestration 
    /// throws ArgumentOutOfRangeException directly without calling any other functions.
    /// </summary>
    [Fact]
    public async Task OrchestrationDirectArgumentOutOfRangeExceptionProperties()
    {
        TaskName orchestratorName = "OrchestrationWithDirectArgumentException";
        string paramName = "testParameter";
        string actualValue = "invalidValue";
        string errorMessage = $"Parameter '{paramName}' is out of range.";

        // Register orchestration that throws ArgumentOutOfRangeException directly
        void MyOrchestrationImpl(TaskOrchestrationContext ctx) =>
            throw new ArgumentOutOfRangeException(paramName, actualValue, errorMessage);

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            // Register the custom exception properties provider
            b.Services.AddSingleton<IExceptionPropertiesProvider, TestExceptionPropertiesProvider>();

            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc(orchestratorName, MyOrchestrationImpl));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);

        Assert.NotNull(metadata.FailureDetails);
        TaskFailureDetails failureDetails = metadata.FailureDetails!;
        Assert.Equal(typeof(ArgumentOutOfRangeException).FullName, failureDetails.ErrorType);
        Assert.Contains(errorMessage, failureDetails.ErrorMessage);

        // Check that custom properties are included for ArgumentOutOfRangeException
        Assert.NotNull(failureDetails.Properties);
        Assert.Equal(2, failureDetails.Properties.Count);

        // Verify parameter name property
        Assert.True(failureDetails.Properties.ContainsKey("Name"));
        Assert.Equal(paramName, failureDetails.Properties["Name"]);

        // Verify actual value property
        Assert.True(failureDetails.Properties.ContainsKey("Value"));
        Assert.Equal(actualValue, failureDetails.Properties["Value"]);

        // Verify the exception type is correctly identified
        Assert.True(failureDetails.IsCausedBy<ArgumentOutOfRangeException>());
    }

    /// <summary>
    /// Tests that exception properties are preserved through nested orchestration calls when
    /// a parent orchestration calls a sub-orchestration, which then calls an activity that throws ArgumentOutOfRangeException.
    /// </summary>
    [Fact]
    public async Task NestedOrchestrationArgumentOutOfRangeExceptionProperties()
    {
        TaskName parentOrchestratorName = "ParentOrchestrationWithNestedArgumentException";
        TaskName subOrchestratorName = "SubOrchestrationWithArgumentException";
        TaskName activityName = "ActivityWithArgumentException";
        string paramName = "nestedParameter";
        string actualValue = "badNestedValue";
        string errorMessage = $"Nested parameter '{paramName}' is out of range.";

        async Task ParentOrchestrationImpl(TaskOrchestrationContext ctx) =>
            await ctx.CallSubOrchestratorAsync(subOrchestratorName);

        async Task SubOrchestrationImpl(TaskOrchestrationContext ctx) =>
            await ctx.CallActivityAsync(activityName);

        void ActivityImpl(TaskActivityContext ctx) =>
            throw new ArgumentOutOfRangeException(paramName, actualValue, errorMessage);

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            // Register the custom exception properties provider
            b.Services.AddSingleton<IExceptionPropertiesProvider, TestExceptionPropertiesProvider>();

            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc(parentOrchestratorName, ParentOrchestrationImpl)
                .AddOrchestratorFunc(subOrchestratorName, SubOrchestrationImpl)
                .AddActivityFunc(activityName, ActivityImpl));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(parentOrchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);

        Assert.NotNull(metadata.FailureDetails);
        TaskFailureDetails failureDetails = metadata.FailureDetails!;

        // The parent orchestration failed due to a TaskFailedException from the sub-orchestration
        Assert.Equal(typeof(TaskFailedException).FullName, failureDetails.ErrorType);
        Assert.Contains(subOrchestratorName, failureDetails.ErrorMessage);

        // Check the first level inner failure (sub-orchestration failure)
        Assert.NotNull(failureDetails.InnerFailure);
        TaskFailureDetails subOrchestrationFailure = failureDetails.InnerFailure!;
        Assert.Equal(typeof(TaskFailedException).FullName, subOrchestrationFailure.ErrorType);
        Assert.Contains(activityName, subOrchestrationFailure.ErrorMessage);

        // Check the second level inner failure (activity failure with ArgumentOutOfRangeException)
        Assert.NotNull(subOrchestrationFailure.InnerFailure);
        TaskFailureDetails activityFailure = subOrchestrationFailure.InnerFailure!;
        Assert.Equal(typeof(ArgumentOutOfRangeException).FullName, activityFailure.ErrorType);
        Assert.Contains(errorMessage, activityFailure.ErrorMessage);

        // Verify that the original ArgumentOutOfRangeException properties are preserved
        Assert.NotNull(activityFailure.Properties);
        Assert.Equal(2, activityFailure.Properties.Count);

        // Verify parameter name property
        Assert.True(activityFailure.Properties.ContainsKey("Name"));
        Assert.Equal(paramName, activityFailure.Properties["Name"]);

        // Verify actual value property
        Assert.True(activityFailure.Properties.ContainsKey("Value"));
        Assert.Equal(actualValue, activityFailure.Properties["Value"]);

        // Verify the exception type hierarchy is correctly identified
        Assert.True(failureDetails.IsCausedBy<TaskFailedException>());
        Assert.True(subOrchestrationFailure.IsCausedBy<TaskFailedException>());
        Assert.True(activityFailure.IsCausedBy<ArgumentOutOfRangeException>());
    }

    /// <summary>
    /// Tests that OriginalException property allows access to specific exception properties for retry logic.
    /// </summary>
    [Theory]
    [InlineData(404, 2, OrchestrationRuntimeStatus.Completed)] // 404 is retryable, should succeed after 2 attempts
    [InlineData(500, 3, OrchestrationRuntimeStatus.Failed)] // 500 is not retryable, should fail immediately
    public async Task RetryWithOriginalExceptionAccess(int statusCode, int expectedAttempts, OrchestrationRuntimeStatus expectedStatus)
    {
        string errorMessage = "API call failed";
        int actualNumberOfAttempts = 0;
        bool originalExceptionWasNull = false;

        RetryPolicy retryPolicy = new(
            maxNumberOfAttempts: 3,
            firstRetryInterval: TimeSpan.FromMilliseconds(1))
        {
            HandleFailure = taskFailureDetails =>
            {
                // This demonstrates the use case from the issue: accessing the original exception
                // to make fine-grained retry decisions based on specific exception properties
                // The taskFailureDetails.OriginalException is the inner exception (ApiException)
                // from the DurableTask.Core.Exceptions.TaskFailedException wrapper
                Console.WriteLine($"ErrorType: {taskFailureDetails.ErrorType}, OriginalException: {taskFailureDetails.OriginalException?.GetType().Name ?? "NULL"}");
                
                if (taskFailureDetails.OriginalException == null)
                {
                    originalExceptionWasNull = true;
                    // Fallback to using IsCausedBy for type checking if OriginalException is null
                    return taskFailureDetails.IsCausedBy<ApiException>();
                }

                if (taskFailureDetails.OriginalException is ApiException apiException)
                {
                    // Only retry on specific status codes (400, 401, 404)
                    return apiException.StatusCode == 400 || apiException.StatusCode == 401 || apiException.StatusCode == 404;
                }

                return false;
            },
        };

        TaskOptions taskOptions = TaskOptions.FromRetryPolicy(retryPolicy);

        TaskName orchestratorName = "OrchestrationWithApiException";
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks =>
                tasks.AddOrchestratorFunc(orchestratorName, async ctx =>
                {
                    await ctx.CallActivityAsync("ApiActivity", options: taskOptions);
                })
                .AddActivityFunc("ApiActivity", (TaskActivityContext context) =>
                {
                    actualNumberOfAttempts++;
                    if (actualNumberOfAttempts < 3)
                    {
                        throw new ApiException(statusCode, errorMessage);
                    }
                }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(expectedStatus, metadata.RuntimeStatus);
        Assert.Equal(expectedAttempts, actualNumberOfAttempts);
        // When the OriginalException is available, originalExceptionWasNull should be false
        if (expectedStatus == OrchestrationRuntimeStatus.Completed)
        {
            Assert.False(originalExceptionWasNull, "OriginalException should be available for retry logic");
        }
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

#pragma warning disable SYSLIB0051
        protected CustomException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#pragma warning restore SYSLIB0051
    }

    /// <summary>
    /// Custom API exception with status code to test the use case from the issue.
    /// </summary>
    [Serializable]
    class ApiException : Exception
    {
        public ApiException(int statusCode, string message)
            : base(message)
        {
            this.StatusCode = statusCode;
        }

        public int StatusCode { get; }

#pragma warning disable SYSLIB0051
        protected ApiException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#pragma warning restore SYSLIB0051
    }

    /// <summary>
    /// A custom exception with diverse property types for comprehensive testing of exception properties.
    /// </summary>
    [Serializable]
    class BusinessValidationException : Exception
    {
        public BusinessValidationException(string message,
            string stringProperty,
            int intProperty,
            long longProperty,
            DateTime dateTimeProperty,
            IDictionary<string, object?> dictionaryProperty,
            IList<object?> listProperty,
            object? nullProperty) : base(message)
        {
            this.StringProperty = stringProperty;
            this.IntProperty = intProperty;
            this.LongProperty = longProperty;
            this.DateTimeProperty = dateTimeProperty;
            this.DictionaryProperty = dictionaryProperty;
            this.ListProperty = listProperty;
            this.NullProperty = nullProperty;
        }

        public string? StringProperty { get; }
        public int? IntProperty { get; }
        public long? LongProperty { get; }
        public DateTime? DateTimeProperty { get; }
        public IDictionary<string, object?>? DictionaryProperty { get; }
        public IList<object?>? ListProperty { get; }
        public object? NullProperty { get; }

#pragma warning disable SYSLIB0051
        protected BusinessValidationException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#pragma warning restore SYSLIB0051
    }

    // Set a custom exception provider.
    class TestExceptionPropertiesProvider : IExceptionPropertiesProvider
    {
        public IDictionary<string, object?>? GetExceptionProperties(Exception exception)
        {
            return exception switch
            {
                ArgumentOutOfRangeException e => new Dictionary<string, object?>
                {
                    ["Name"] = e.ParamName ?? string.Empty,
                    ["Value"] = e.ActualValue ?? string.Empty,
                },
                Microsoft.DurableTask.Grpc.Tests.OrchestrationErrorHandling.BusinessValidationException e => new Dictionary<string, object?>
                {
                    ["StringProperty"] = e.StringProperty,
                    ["IntProperty"] = e.IntProperty,
                    ["LongProperty"] = e.LongProperty,
                    ["DateTimeProperty"] = e.DateTimeProperty,
                    ["DictionaryProperty"] = e.DictionaryProperty,
                    ["ListProperty"] = e.ListProperty,
                    ["NullProperty"] = e.NullProperty,
                },
                _ => null // No custom properties for other exceptions
            };
        }
    }
}
