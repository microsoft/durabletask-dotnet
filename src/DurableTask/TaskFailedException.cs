// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core.Exceptions;

namespace Microsoft.DurableTask;

/// <summary>
/// Exception that gets thrown when when a durable task, such as an activity or a sub-orchestration, fails with an
/// unhandled exception.
/// </summary>
/// <remarks>
/// Detailed information associated with a particular task failure, including exception details, can be found in the 
/// <see cref="FailureDetails"/> property.
/// </remarks>
public sealed class TaskFailedException : Exception
{
    internal TaskFailedException(string taskName, int taskId, OrchestrationException cause)
        : base(GetExceptionMessage(taskName, taskId, cause))
    {
        this.TaskName = taskName;
        this.TaskId = taskId;
        this.FailureDetails = TaskFailureDetails.FromCoreException(cause);
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
    /// auto-incrementing ID associated with it. This is used to distinguish tasks from one another, even if, for
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
    public bool IsCausedByException<T>() where T : Exception => this.FailureDetails.ErrorType == typeof(T).FullName;

    static string GetExceptionMessage(string taskName, int taskId, OrchestrationException cause)
    {
        // NOTE: Some integration tests depend on the format of this exception message.
        string subMessage = cause.FailureDetails?.ErrorMessage ?? cause.Message;
        return $"Task '{taskName}' (#{taskId}) failed with an unhandled exception: {subMessage}";
    }
}
