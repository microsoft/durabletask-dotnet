// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using CoreOrchestrationException = DurableTask.Core.Exceptions.OrchestrationException;

namespace Microsoft.DurableTask;

/// <summary>
/// Exception that gets thrown when a durable task sub-orchestration, fails with an
/// unhandled exception.
/// </summary>
/// <remarks>
/// Detailed information associated with a particular task failure, including exception details, can be found in the
/// <see cref="FailureDetails"/> property.
/// </remarks>
public sealed class SubOrchestrationFailedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SubOrchestrationFailedException"/> class.
    /// </summary>
    /// <param name="taskName">The failed sub-orchestrationname.</param>
    /// <param name="taskId">The task ID.</param>
    /// <param name="failureDetails">The failure details.</param>
    /// <param name="innerException">The inner exception.</param>
    public SubOrchestrationFailedException(string taskName, int taskId, TaskFailureDetails failureDetails, Exception innerException)
       : base(GetExceptionMessage(taskName, taskId, failureDetails, innerException), FromExceptionRecursive(innerException))
    {
        this.TaskName = taskName;
        this.TaskId = taskId;
        this.FailureDetails = failureDetails;
    }

    /// <summary>
    /// Gets the name of the failed sub-orchestration.
    /// </summary>
    public string TaskName { get; }

    /// <summary>
    /// Gets the ID of the failed sub-orchestration.
    /// </summary>
    /// <remarks>
    /// Each durable task (activities, timers, sub-orchestrations, etc.) scheduled by a task orchestrator has an
    /// auto-incrementing ID associated with it. This ID is used to distinguish tasks from one another, even if, for
    /// example, they are tasks that call the same activity. This ID can therefore be used to more easily correlate a
    /// specific task failure to a specific task.
    /// </remarks>
    public int TaskId { get; }

    /// <summary>
    /// Gets the details of the task failure, including exception information.
    /// </summary>
    public TaskFailureDetails FailureDetails { get; }

    // This method is the same as the one in `TaskFailedException` to keep the exception message format consistent.
    static string GetExceptionMessage(string taskName, int taskId, TaskFailureDetails? details, Exception? cause)
    {
        // NOTE: Some integration tests depend on the format of this exception message.
        string? subMessage = null;
        if (details is not null)
        {
            subMessage = details.ErrorMessage;
        }
        else if (cause is global::DurableTask.Core.Exceptions.OrchestrationException coreEx)
        {
            subMessage = coreEx.FailureDetails?.ErrorMessage;
        }

        if (subMessage is null)
        {
            subMessage = cause?.Message;
        }

        return subMessage is null
            ? $"Task '{taskName}' (#{taskId}) failed with an unhandled exception."
            : $"Task '{taskName}' (#{taskId}) failed with an unhandled exception: {subMessage}";
    }

    static Exception? FromExceptionRecursive(Exception? exception)
    {
        if (exception is null)
        {
            return null;
        }

        if (exception is CoreOrchestrationException)
        {
            // is always null!!!
            return FromExceptionRecursive(exception.InnerException);
        }

        return exception;
    }
}
