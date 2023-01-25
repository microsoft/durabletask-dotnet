// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Represents the base request to run a <see cref="ITaskOrchestrator" />.
/// </summary>
public interface IBaseOrchestrationRequest
{
    /// <summary>
    /// Gets the <see cref="TaskName" /> representing the <see cref="ITaskOrchestrator" /> to run.
    /// </summary>
    /// <returns>A <see cref="TaskName" />.</returns>
    /// <remarks>
    /// This is a function instead of a property so it is excluded in serialization without needing to use a
    /// serialization library specific attribute to exclude it.
    /// </remarks>
    TaskName GetTaskName();
}

/// <summary>
/// Represents a request to run a <see cref="ITaskOrchestrator" /> which returns <typeparamref name="TResult" />.
/// </summary>
/// <typeparam name="TResult">The result of the orchestrator that is to be ran.</typeparam>
public interface IOrchestrationRequest<out TResult> : IBaseOrchestrationRequest
{
}

/// <summary>
/// Represents a request to run a <see cref="ITaskOrchestrator" /> which has no return.
/// </summary>
public interface IOrchestrationRequest : IOrchestrationRequest<Unit>
{
}
