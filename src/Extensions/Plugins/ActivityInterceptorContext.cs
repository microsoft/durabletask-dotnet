// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Plugins;

/// <summary>
/// Context provided to activity interceptors during lifecycle events.
/// </summary>
public sealed class ActivityInterceptorContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityInterceptorContext"/> class.
    /// </summary>
    /// <param name="name">The name of the activity.</param>
    /// <param name="instanceId">The orchestration instance ID that scheduled this activity.</param>
    /// <param name="input">The activity input.</param>
    public ActivityInterceptorContext(TaskName name, string instanceId, object? input)
    {
        this.Name = name;
        this.InstanceId = instanceId;
        this.Input = input;
    }

    /// <summary>
    /// Gets the name of the activity.
    /// </summary>
    public TaskName Name { get; }

    /// <summary>
    /// Gets the orchestration instance ID that scheduled this activity.
    /// </summary>
    public string InstanceId { get; }

    /// <summary>
    /// Gets the activity input.
    /// </summary>
    public object? Input { get; }

    /// <summary>
    /// Gets a dictionary that can be used to pass data between interceptors during a single execution.
    /// </summary>
    public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>();
}
