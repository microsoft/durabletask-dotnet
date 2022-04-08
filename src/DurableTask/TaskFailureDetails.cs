// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Microsoft.DurableTask;

/// <summary>
/// Record that represents the details of a task failure.
/// </summary>
/// <param name="ErrorType">The error type. For .NET, this is the namespace-qualified exception type name.</param>
/// <param name="ErrorMessage">A summary description of the failure.</param>
/// <param name="StackTrace">The stack trace of the failure.</param>
/// <param name="InnerFailure">The inner cause of the task failure.</param>
public record TaskFailureDetails(string ErrorType, string ErrorMessage, string? StackTrace, TaskFailureDetails? InnerFailure)
{
    Type? exceptionType;

    /// <summary>
    /// Gets a debug-friendly description of the failure information.
    /// </summary>
    public override string ToString()
    {
        return $"{this.ErrorType}: {this.ErrorMessage}";
    }

    /// <summary>
    /// Returns <c>true</c> if the task failure was provided by the specified exception type.
    /// </summary>
    /// <remarks>
    /// This method allows checking if a task failed due to an exception of a specific type by attempting
    /// to load the type specified in <see cref="ErrorType"/>. If the exception type cannot be loaded
    /// for any reason, this method will return <c>false</c>. Base types are supported.
    /// </remarks>
    /// <typeparam name="T">The type of exception to test against.</typeparam>
    /// <returns>Returns <c>true</c> if the <see cref="ErrorType"/> value matches <typeparamref name="T"/>; <c>false</c> otherwise.</returns>
    public bool IsCausedBy<T>() where T : Exception
    {
        this.exceptionType ??= Type.GetType(this.ErrorType, throwOnError: false);
        return this.exceptionType != null && typeof(T).IsAssignableFrom(this.exceptionType);
    }

    internal static TaskFailureDetails FromCoreException(global::DurableTask.Core.Exceptions.OrchestrationException e)
    {
        return new TaskFailureDetails(
            e.FailureDetails?.ErrorType ?? "(unknown)",
            e.FailureDetails?.ErrorMessage ?? "(unknown)",
            e.FailureDetails?.StackTrace,
            null /* InnerFailure */);
    }
}
