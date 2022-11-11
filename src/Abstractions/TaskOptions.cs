// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Options that can be used to control the behavior of orchestrator task execution.
/// </summary>
/// <param name="Retry">The retry options. <c>null</c> for no retries.</param>
public record TaskOptions(TaskRetryOptions? Retry = null)
{
    /// <summary>
    /// Returns a new <see cref="TaskOptions" /> from the provided <see cref="RetryPolicy" />.
    /// </summary>
    /// <param name="policy">The policy to convert from.</param>
    /// <returns>A <see cref="TaskOptions" /> built from the policy.</returns>
    public static TaskOptions FromRetryPolicy(RetryPolicy policy) => new(policy);

    /// <summary>
    /// Returns a new <see cref="TaskOptions" /> from the provided <see cref="AsyncRetryHandler" />.
    /// </summary>
    /// <param name="handler">The handler to convert from.</param>
    /// <returns>A <see cref="TaskOptions" /> built from the handler.</returns>
    public static TaskOptions FromRetryHandler(AsyncRetryHandler handler) => new(handler);

    /// <summary>
    /// Returns a new <see cref="TaskOptions" /> from the provided <see cref="RetryHandler" />.
    /// </summary>
    /// <param name="handler">The handler to convert from.</param>
    /// <returns>A <see cref="TaskOptions" /> built from the handler.</returns>
    public static TaskOptions FromRetryHandler(RetryHandler handler)
    {
        Check.NotNull(handler);
        return FromRetryHandler(context => Task.FromResult(handler.Invoke(context)));
    }

    /// <summary>
    /// Gets the instance ID, if available, for this options instance.
    /// </summary>
    /// <returns>The orchestration instance ID if available, <c>null</c> otherwise.</returns>
    internal string? GetInstanceId()
    {
        return this is OrchestrationOptions options ? options.InstanceId : null;
    }
}

/// <summary>
/// Options that can be used to control the behavior of orchestrator task execution. This derived type can be used to
/// supply extra options for orchestrations.
/// </summary>
/// <param name="InstanceId">The orchestration instance ID to use. <c>null</c> to have one generated.</param>
/// <param name="Retry">The retry options. <c>null</c> for no retries.</param>
public record OrchestrationOptions(string? InstanceId = null, TaskRetryOptions? Retry = null)
    : TaskOptions(Retry);

/// <summary>
/// Options for submitting new orchestrations via the client.
/// </summary>
/// <param name="InstanceId">
/// The unique ID of the orchestration instance to schedule. If not specified, a new GUID value is used.
/// </param>
/// <param name="StartAt">
/// The time when the orchestration instance should start executing. If not specified or if a date-time in the past
/// is specified, the orchestration instance will be scheduled immediately.
/// </param>
public record StartOrchestrationOptions(string? InstanceId = null, DateTimeOffset? StartAt = null);
