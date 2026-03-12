// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Plugins;

/// <summary>
/// Context provided to orchestration interceptors during lifecycle events.
/// </summary>
public sealed class OrchestrationInterceptorContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestrationInterceptorContext"/> class.
    /// </summary>
    /// <param name="name">The name of the orchestration.</param>
    /// <param name="instanceId">The instance ID of the orchestration.</param>
    /// <param name="isReplaying">Whether this is a replay execution.</param>
    /// <param name="input">The orchestration input.</param>
    public OrchestrationInterceptorContext(TaskName name, string instanceId, bool isReplaying, object? input)
    {
        this.Name = name;
        this.InstanceId = instanceId;
        this.IsReplaying = isReplaying;
        this.Input = input;
    }

    /// <summary>
    /// Gets the name of the orchestration.
    /// </summary>
    public TaskName Name { get; }

    /// <summary>
    /// Gets the instance ID of the orchestration.
    /// </summary>
    public string InstanceId { get; }

    /// <summary>
    /// Gets a value indicating whether this execution is a replay.
    /// </summary>
    public bool IsReplaying { get; }

    /// <summary>
    /// Gets the orchestration input.
    /// </summary>
    public object? Input { get; }

    /// <summary>
    /// Gets a dictionary that can be used to pass data between interceptors during a single execution.
    /// </summary>
    public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>();
}
