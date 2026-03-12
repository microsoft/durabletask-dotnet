// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Plugins.BuiltIn;

/// <summary>
/// Context for authorization decisions.
/// </summary>
public sealed class AuthorizationContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationContext"/> class.
    /// </summary>
    /// <param name="name">The task name.</param>
    /// <param name="instanceId">The orchestration instance ID.</param>
    /// <param name="targetType">The type of target (orchestration or activity).</param>
    /// <param name="input">The task input.</param>
    public AuthorizationContext(TaskName name, string instanceId, AuthorizationTargetType targetType, object? input)
    {
        this.Name = name;
        this.InstanceId = instanceId;
        this.TargetType = targetType;
        this.Input = input;
    }

    /// <summary>
    /// Gets the task name.
    /// </summary>
    public TaskName Name { get; }

    /// <summary>
    /// Gets the orchestration instance ID.
    /// </summary>
    public string InstanceId { get; }

    /// <summary>
    /// Gets the type of target being authorized.
    /// </summary>
    public AuthorizationTargetType TargetType { get; }

    /// <summary>
    /// Gets the task input.
    /// </summary>
    public object? Input { get; }
}
