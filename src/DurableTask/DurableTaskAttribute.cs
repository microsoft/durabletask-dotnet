// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Microsoft.DurableTask;

/// <summary>
/// Indicates that the attributed class represents a durable task.
/// </summary>
/// <remarks>
/// This attribute is meant to be used on class definitions that derive from
/// <see cref="TaskOrchestratorBase{TInput, TOutput}"/> or
/// <see cref="TaskActivityBase{TInput, TOutput}"/>. It is used specifically
/// by build-time source generators to generate type-safe methods for invoking
/// orchestratos or activities.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DurableTaskAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DurableTaskAttribute"/> class.
    /// </summary>
    /// <param name="name">
    /// The name of the durable task. If not specified, the class name is used as the implied name of the durable task.
    /// </param>
    public DurableTaskAttribute(string? name = null)
    {
        this.Name = name ?? string.Empty;
    }

    /// <summary>
    /// Gets the name of the durable task.
    /// </summary>
    public TaskName Name { get; }
}
