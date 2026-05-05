// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.Middleware;

/// <summary>
/// Provides orchestration execution details to task orchestration middleware.
/// </summary>
public abstract class TaskOrchestrationMiddlewareContext
{
    /// <summary>
    /// Gets the name of the orchestration.
    /// </summary>
    public abstract TaskName Name { get; }

    /// <summary>
    /// Gets the unique orchestration instance ID.
    /// </summary>
    public abstract string InstanceId { get; }

    /// <summary>
    /// Gets the version of the orchestration instance.
    /// </summary>
    public abstract string Version { get; }

    /// <summary>
    /// Gets the parent orchestration instance, or <c>null</c> when this orchestration has no parent.
    /// </summary>
    public abstract ParentOrchestrationInstance? Parent { get; }

    /// <summary>
    /// Gets the orchestration tags, if any.
    /// </summary>
    public abstract IReadOnlyDictionary<string, string>? Tags { get; }

    /// <summary>
    /// Gets a value indicating whether the orchestration is replaying prior execution history.
    /// </summary>
    public abstract bool IsReplaying { get; }

    /// <summary>
    /// Gets the declared type of the orchestration input.
    /// </summary>
    public abstract Type InputType { get; }

    /// <summary>
    /// Gets the deserialized orchestration input.
    /// </summary>
    public abstract object? Input { get; }

    /// <summary>
    /// Gets the raw serialized orchestration input, if available.
    /// </summary>
    public abstract string? RawInput { get; }

    /// <summary>
    /// Gets the orchestration context used by the orchestrator implementation.
    /// </summary>
    public abstract TaskOrchestrationContext OrchestrationContext { get; }

    /// <summary>
    /// Gets the host-side features for the orchestration work item.
    /// </summary>
    public abstract IMiddlewareFeatures Features { get; }

    /// <summary>
    /// Gets a cancellation token that is canceled when orchestration execution should stop.
    /// </summary>
    public abstract CancellationToken CancellationToken { get; }

    /// <summary>
    /// Gets the orchestration result. The value is populated after the next middleware delegate returns.
    /// </summary>
    public abstract object? Result { get; }

    /// <summary>
    /// Gets the orchestration input as the requested type.
    /// </summary>
    /// <typeparam name="T">The expected input type.</typeparam>
    /// <returns>
    /// The orchestration input as type <typeparamref name="T"/>. If <see cref="Input"/> is <c>null</c>, returns the
    /// default value for <typeparamref name="T"/>.
    /// </returns>
    /// <exception cref="InvalidCastException">
    /// Thrown when <see cref="Input"/> is not assignable to <typeparamref name="T"/>.
    /// </exception>
    public virtual T? GetInput<T>()
    {
        if (this.Input is null)
        {
            return default;
        }

        if (this.Input is T input)
        {
            return input;
        }

        throw new InvalidCastException(
            $"The orchestration middleware input type '{this.Input.GetType()}' cannot be assigned to '{typeof(T)}'.");
    }
}
