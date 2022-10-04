// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Exception that gets thrown when a durable task, such as an activity or a sub-orchestration, fails with an
/// unhandled exception.
/// </summary>
/// <remarks>
/// Detailed information associated with a particular task failure, including exception details, can be found in the
/// <see cref="FailureDetails"/> property.
/// </remarks>
public sealed class TaskFailedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TaskFailedException"/> class.
    /// </summary>
    /// <param name="taskName">The task name.</param>
    /// <param name="taskId">The task ID.</param>
    /// <param name="innerException">The inner exception.</param>
    public TaskFailedException(string taskName, int taskId, Exception innerException)
        : base(GetExceptionMessage(taskName, taskId, null, innerException), innerException)
    {
        this.TaskName = taskName;
        this.TaskId = taskId;
        this.FailureDetails = TaskFailureDetails.FromException(innerException);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskFailedException"/> class.
    /// </summary>
    /// <param name="taskName">The task name.</param>
    /// <param name="taskId">The task ID.</param>
    /// <param name="failureDetails">The failure details.</param>
    public TaskFailedException(string taskName, int taskId, TaskFailureDetails failureDetails)
        : base(GetExceptionMessage(taskName, taskId, failureDetails, null))
    {
        this.TaskName = taskName;
        this.TaskId = taskId;
        this.FailureDetails = failureDetails;
    }

    /// <summary>
    /// Gets the name of the failed task.
    /// </summary>
    public string TaskName { get; }

    /// <summary>
    /// Gets the ID of the failed task.
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

    /// <summary>
    /// Returns true if the task failure was provided by the specified exception type.
    /// </summary>
    /// <remarks>
    /// This method allows checking if a task failed due to an exception of a specific type.
    /// The comparison relies on a string comparison of the full type name (e.g., "System.InvalidOperationException")
    /// and therefore doesn't support base types.
    /// </remarks>
    /// <typeparam name="T">The type of exception to test against.</typeparam>
    /// <returns>
    /// Returns <c>true</c> if the <see cref="FailureDetails"/>'s <see cref="TaskFailureDetails.ErrorType"/> value
    /// matches <typeparamref name="T"/>; <c>false</c> otherwise.
    /// </returns>
    [Obsolete("Use the FailureDetails property and its IsCausedBy<T>() method")]
    public bool IsCausedByException<T>()
        where T : Exception
        => this.FailureDetails.ErrorType == typeof(T).FullName;

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
}
