// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask;
/// <summary>
/// Options that can be used to control the behavior of orchestrator task execution.
/// </summary>
public record TaskOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TaskOptions"/> class.
    /// </summary>
    /// <param name="retry">The task retry options.</param>
    public TaskOptions(TaskRetryOptions? retry = null)
    {
        this.Retry = retry;
    }

    /// <summary>
    /// Gets the task retry options.
    /// </summary>
    public TaskRetryOptions? Retry { get; init; }

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
    public static TaskOptions FromRetryHandler(RetryHandler handler) => new(handler);

    /// <summary>
    /// Returns a new <see cref="SubOrchestrationOptions" /> with the provided instance ID. This can be used when
    /// starting a new sub-orchestration to specify the instance ID.
    /// </summary>
    /// <param name="instanceId">The instance ID to use.</param>
    /// <returns>A new <see cref="SubOrchestrationOptions" />.</returns>
    public SubOrchestrationOptions WithInstanceId(string instanceId) => new(this, instanceId);
}

/// <summary>
/// Options that can be used to control the behavior of orchestrator task execution. This derived type can be used to
/// supply extra options for orchestrations.
/// </summary>
public record SubOrchestrationOptions : TaskOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SubOrchestrationOptions"/> class.
    /// </summary>
    /// <param name="retry">The task retry options.</param>
    /// <param name="instanceId">The orchestration instance ID.</param>
    public SubOrchestrationOptions(TaskRetryOptions? retry = null, string? instanceId = null)
        : base(retry)
    {
        this.InstanceId = instanceId;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SubOrchestrationOptions"/> class.
    /// </summary>
    /// <param name="options">The task options to wrap.</param>
    /// <param name="instanceId">The orchestration instance ID.</param>
    public SubOrchestrationOptions(TaskOptions options, string? instanceId = null)
        : base(options)
    {
        this.InstanceId = instanceId;
        if (instanceId is null && options is SubOrchestrationOptions derived)
        {
            this.InstanceId = derived.InstanceId;
        }
    }

    /// <summary>
    /// Gets the orchestration instance ID.
    /// </summary>
    public string? InstanceId { get; init; }
}

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
/// <param name="OrchestrationIdReusePolicy">The orchestration reuse policy. This allows for the reuse of an instance ID
/// as well as the options for it.</param>
public record StartOrchestrationOptions(string? InstanceId = null, DateTimeOffset? StartAt = null, Dictionary<P.OrchestrationStatus, P.CreateOrchestrationAction>?
 OrchestrationIdReusePolicy = null);
