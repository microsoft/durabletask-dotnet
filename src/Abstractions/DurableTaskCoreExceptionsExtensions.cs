// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Abstractions;

/// <summary>
/// Extension methods realated to the global::DurableTask.Core namespace items.
/// </summary>
static class DurableTaskCoreExceptionsExtensions
{
    /// <summary>
    /// Converts <paramref name="taskFailedException"/> to a <see cref="TaskFailureDetails"/> instance.
    /// If <paramref name="taskFailedException"/> does not contain FailureDetails, null shall be returned.
    /// </summary>
    /// <param name="taskFailedException"><see cref="global::DurableTask.Core.Exceptions.TaskFailedException"/> instance.</param>
    /// <returns>
    /// A <see cref="TaskFailureDetails"/> instance if <paramref name="taskFailedException"/> contains
    /// FailureDetails; otherwise, null is returned.
    /// </returns>
    internal static TaskFailureDetails? ToTaskFailureDetails(this global::DurableTask.Core.Exceptions.TaskFailedException taskFailedException)
        => taskFailedException.FailureDetails.ToTaskFailureDetails(taskFailedException);

    /// <summary>
    /// Converts <paramref name="subOrchestrationFailedException"/> to a <see cref="TaskFailureDetails"/> instance.
    /// If <paramref name="subOrchestrationFailedException"/> does not contain FailureDetails, null shall be returned.
    /// </summary>
    /// <param name="subOrchestrationFailedException"><see cref="global::DurableTask.Core.Exceptions.SubOrchestrationFailedException"/> instance.</param>
    /// <returns>
    /// A <see cref="TaskFailureDetails"/> instance if <paramref name="subOrchestrationFailedException"/> contains
    /// FailureDetails; otherwise, null is returned.
    /// </returns>
    internal static TaskFailureDetails? ToTaskFailureDetails(this global::DurableTask.Core.Exceptions.SubOrchestrationFailedException subOrchestrationFailedException) => subOrchestrationFailedException.FailureDetails.ToTaskFailureDetails(subOrchestrationFailedException);

    /// <summary>
    /// Converts <paramref name="failureDetails"/> to a <see cref="TaskFailureDetails"/> instance.
    /// </summary>
    /// <param name="failureDetails"><see cref="global::DurableTask.Core.FailureDetails"/> instance.</param>
    /// <param name="originalException">Optional original exception that caused the failure.</param>
    /// <returns>
    /// A <see cref="TaskFailureDetails"/> instance if <paramref name="failureDetails"/> is not null; otherwise, null.
    /// </returns>
    internal static TaskFailureDetails? ToTaskFailureDetails(this global::DurableTask.Core.FailureDetails? failureDetails, Exception? originalException = null)
    {
        if (failureDetails is null)
        {
            return null;
        }

        // The originalException passed in is the Core exception wrapper (TaskFailedException or SubOrchestrationFailedException)
        // Its InnerException is the actual user exception that we want to expose
        Exception? actualException = originalException?.InnerException;

        return new TaskFailureDetails(
            failureDetails.ErrorType,
            failureDetails.ErrorMessage,
            failureDetails.StackTrace,
            failureDetails.InnerFailure?.ToTaskFailureDetails(actualException?.InnerException),
            failureDetails.Properties)
        {
            OriginalException = actualException,
        };
    }
}
