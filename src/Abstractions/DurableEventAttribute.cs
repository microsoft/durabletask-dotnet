// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Indicates that the attributed type represents a durable event.
/// </summary>
/// <remarks>
/// This attribute is meant to be used on type definitions to generate strongly-typed
/// external event methods for orchestration contexts.
/// It is used specifically by build-time source generators to generate type-safe methods for waiting
/// for external events in orchestrations.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class DurableEventAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DurableEventAttribute"/> class.
    /// </summary>
    /// <param name="name">
    /// The name of the durable event. If not specified, the type name is used as the implied name of the durable event.
    /// </param>
    public DurableEventAttribute(string? name = null)
    {
        // This logic cannot become too complex as code-generator relies on examining the constructor arguments.
        this.Name = string.IsNullOrEmpty(name) ? default : new TaskName(name!);
    }

    /// <summary>
    /// Gets the name of the durable event.
    /// </summary>
    public TaskName Name { get; }
}
