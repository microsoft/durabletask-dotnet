// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using CoreFailureDetails = DurableTask.Core.FailureDetails;
using CoreOrchestrationException = DurableTask.Core.Exceptions.OrchestrationException;

namespace Microsoft.DurableTask;

/// <summary>
/// Record that represents the details of a task failure.
/// </summary>
/// <param name="ErrorType">The error type. For .NET, this is the namespace-qualified exception type name.</param>
/// <param name="ErrorMessage">A summary description of the failure.</param>
/// <param name="StackTrace">The stack trace of the failure.</param>
/// <param name="InnerFailure">The inner cause of the task failure.</param>
/// <param name="Properties">Additional properties associated with the exception.</param>
public record TaskFailureDetails(string ErrorType, string ErrorMessage, string? StackTrace, TaskFailureDetails? InnerFailure, IDictionary<string, object?>? Properties)
{
    Type? loadedExceptionType;

    /// <summary>
    /// Gets a debug-friendly description of the failure information.
    /// </summary>
    /// <returns>A debugger friendly display string.</returns>
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
    /// <exception cref="AmbiguousMatchException">If multiple exception types with the same name are found.</exception>
    /// <returns>
    /// <c>true</c> if the <see cref="ErrorType"/> value matches <typeparamref name="T"/>; <c>false</c> otherwise.
    /// </returns>
    public bool IsCausedBy<T>() where T : Exception
    {
        return this.IsCausedBy(typeof(T));
    }

    /// <returns>
    /// <c>true</c> if the <see cref="ErrorType"/> value matches <paramref name="targetBaseExceptionType"/>; <c>false</c> otherwise.
    /// </returns>
    /// <exception cref="ArgumentException">If <paramref name="targetBaseExceptionType"/> is not an exception type.</exception>
    /// <inheritdoc cref="IsCausedBy{T}"/>
    public bool IsCausedBy(Type targetBaseExceptionType)
    {
        Check.NotNull(targetBaseExceptionType);

        if (!typeof(Exception).IsAssignableFrom(targetBaseExceptionType))
        {
            throw new ArgumentException($"The type {targetBaseExceptionType} is not an exception type.", nameof(targetBaseExceptionType));
        }

        if (string.IsNullOrEmpty(this.ErrorType))
        {
            return false;
        }

        // This check works for .NET exception types defined in System.Core.PrivateLib (aka mscorelib.dll)
        this.loadedExceptionType ??= Type.GetType(this.ErrorType, throwOnError: false);

        // For exception types defined in the same assembly as the target exception type.
        this.loadedExceptionType ??= targetBaseExceptionType.Assembly.GetType(this.ErrorType, throwOnError: false);

        // For custom exception types defined in the app's assembly
        this.loadedExceptionType ??= Assembly.GetCallingAssembly().GetType(this.ErrorType);

        if (this.loadedExceptionType is null)
        {
            // This last check works for exception types defined in any loaded assembly (e.g. NuGet packages, etc.).
            // This is a fallback that should rarely be needed except in obscure cases.
            List<Type> matchingExceptionTypes = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(this.ErrorType, throwOnError: false))
                .Where(t => t is not null)
                .ToList();
            if (matchingExceptionTypes.Count == 1)
            {
                this.loadedExceptionType = matchingExceptionTypes[0];
            }
            else if (matchingExceptionTypes.Count > 1)
            {
                throw new AmbiguousMatchException($"Multiple exception types with the name '{this.ErrorType}' were found.");
            }
        }

        if (this.loadedExceptionType is null)
        {
            // The actual exception type could not be loaded, so we cannot determine if it matches the target type.
            return false;
        }

        return targetBaseExceptionType.IsAssignableFrom(this.loadedExceptionType);
    }

    /// <summary>
    /// Creates a task failure details from an <see cref="Exception" />.
    /// </summary>
    /// <param name="exception">The exception to use.</param>
    /// <returns>A new task failure details.</returns>
    public static TaskFailureDetails FromException(Exception exception)
    {
        Check.NotNull(exception);
        return FromExceptionRecursive(exception);
    }

    /// <summary>
    /// Converts this task failure details to a <see cref="CoreFailureDetails"/> instance.
    /// </summary>
    /// <returns>A new <see cref="CoreFailureDetails"/> instance.</returns>
    internal CoreFailureDetails ToCoreFailureDetails()
    {
        return new CoreFailureDetails(
            this.ErrorType,
            this.ErrorMessage,
            this.StackTrace,
            this.InnerFailure?.ToCoreFailureDetails(),
            isNonRetriable: false,
            this.Properties);
    }

    /// <summary>
    /// Creates a task failure details from a <see cref="CoreFailureDetails"/> instance.
    /// </summary>
    /// <param name="coreFailureDetails">The core failure details to use.</param>
    /// <returns>A new task failure details.</returns>
    [return: NotNullIfNotNull(nameof(coreFailureDetails))]
    internal static TaskFailureDetails? FromCoreFailureDetails(CoreFailureDetails? coreFailureDetails)
    {
        if (coreFailureDetails is null)
        {
            return null;
        }

        return new TaskFailureDetails(
            coreFailureDetails.ErrorType,
            coreFailureDetails.ErrorMessage,
            coreFailureDetails.StackTrace,
            FromCoreFailureDetails(coreFailureDetails.InnerFailure),
            coreFailureDetails.Properties);
    }

    [return: NotNullIfNotNull(nameof(exception))]
    static TaskFailureDetails? FromExceptionRecursive(Exception? exception)
    {
        if (exception is null)
        {
            return null;
        }

        if (exception is CoreOrchestrationException coreEx)
        {
            return new TaskFailureDetails(
                coreEx.FailureDetails?.ErrorType ?? "(unknown)",
                coreEx.FailureDetails?.ErrorMessage ?? "(unknown)",
                coreEx.FailureDetails?.StackTrace,
                FromCoreFailureDetailsRecursive(coreEx.FailureDetails?.InnerFailure) ?? FromExceptionRecursive(coreEx.InnerException),
                coreEx.FailureDetails?.Properties);
        }

        // might need to udpate this later
        return new TaskFailureDetails(
            exception.GetType().ToString(),
            exception.Message,
            exception.StackTrace,
            FromExceptionRecursive(exception.InnerException),
            null);
    }

    static TaskFailureDetails? FromCoreFailureDetailsRecursive(CoreFailureDetails? coreFailureDetails)
    {
        if (coreFailureDetails is null)
        {
            return null;
        }

        return new TaskFailureDetails(
            coreFailureDetails.ErrorType,
            coreFailureDetails.ErrorMessage,
            coreFailureDetails.StackTrace,
            FromCoreFailureDetailsRecursive(coreFailureDetails.InnerFailure),
            coreFailureDetails.Properties);
    }
}
