// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Represents the base request to run a <see cref="ITaskActivity" />.
/// </summary>
public interface IBaseActivityRequest
{
    /// <summary>
    /// Gets the <see cref="TaskName" /> representing the <see cref="ITaskActivity" /> to run.
    /// </summary>
    /// <returns>A <see cref="TaskName" />.</returns>
    /// <remarks>
    /// This is a function instead of a property so it is excluded in serialization without needing to use a
    /// serialization library specific attribute to exclude it.
    /// </remarks>
    TaskName GetTaskName();
}

/// <summary>
/// Represents a request to run a <see cref="ITaskActivity" /> which returns <typeparamref name="TResult" />.
/// </summary>
/// <typeparam name="TResult">The result of the orchestrator that is to be ran.</typeparam>
public interface IActivityRequest<out TResult> : IBaseActivityRequest
{
}

/// <summary>
/// Represents a request to run a <see cref="ITaskActivity" /> which has no return.
/// </summary>
public interface IActivityRequest : IActivityRequest<Unit>
{
}

/// <summary>
/// Helpers for creating activity requests.
/// </summary>
public static class ActivityRequest
{
    /// <summary>
    /// Gets an <see cref="IActivityRequest{TResult}" /> which has an explicitly provided input.
    /// </summary>
    /// <remarks>
    /// This is useful when you want to use an existing type for input (like <see cref="string" />) and not derive an
    /// entirely new type.
    /// </remarks>
    /// <typeparam name="TResult">The result type of the activity.</typeparam>
    /// <param name="name">The name of the activity to run.</param>
    /// <param name="input">The input for the activity.</param>
    /// <returns>A request that can be used to enqueue an activity.</returns>
    public static IActivityRequest<TResult> Create<TResult>(TaskName name, object? input = null)
        => new Request<TResult>(name, input);

    /// <summary>
    /// Represents an activity request where the input is not the request itself.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    class Request<TResult> : IActivityRequest<TResult>, IProvidesInput
    {
        readonly TaskName name;
        readonly object? input;

        /// <summary>
        /// Initializes a new instance of the <see cref="Request{TResult}"/> class.
        /// </summary>
        /// <param name="name">The task name.</param>
        /// <param name="input">The input.</param>
        public Request(TaskName name, object? input)
        {
            this.name = name;
            this.input = input;
        }

        /// <inheritdoc/>
        public object? GetInput() => this.input;

        /// <inheritdoc/>
        public TaskName GetTaskName() => this.name;
    }
}
