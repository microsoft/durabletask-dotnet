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
    /// Gets an <see cref="IActivityRequest" /> which has an explicitly provided input.
    /// </summary>
    /// <remarks>
    /// This is useful when you want to use an existing type for input (like <see cref="string" />) and not derive an
    /// entirely new type.
    /// </remarks>
    /// <param name="name">The name of the activity to run.</param>
    /// <param name="input">The input for the activity.</param>
    /// <returns>A request that can be used to enqueue an activity.</returns>
    public static IActivityRequest Create(TaskName name, object? input = null)
        => new Request(name, input);

    /// <summary>
    /// Gets the activity input from a <see cref="IBaseActivityRequest" />.
    /// </summary>
    /// <param name="request">The request to get input for.</param>
    /// <returns>The input.</returns>
    internal static object? GetInput(this IBaseActivityRequest request)
    {
        if (request is IProvidesInput provider)
        {
            return provider.GetInput();
        }

        return request;
    }

    class Request<TResult> : RequestCore, IActivityRequest<TResult>
    {
        public Request(TaskName name, object? input)
            : base(name, input)
        {
        }
    }

    class Request : RequestCore, IActivityRequest
    {
        public Request(TaskName name, object? input)
            : base(name, input)
        {
        }
    }

    class RequestCore : IBaseActivityRequest, IProvidesInput
    {
        readonly TaskName name;
        readonly object? input;

        public RequestCore(TaskName name, object? input)
        {
            this.name = name;
            this.input = input;
        }

        public object? GetInput() => this.input;

        public TaskName GetTaskName() => this.name;
    }
}
