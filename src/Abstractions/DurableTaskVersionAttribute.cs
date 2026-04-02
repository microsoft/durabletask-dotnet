// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Indicates the version of a class-based durable orchestrator.
/// </summary>
/// <remarks>
/// This attribute is only consumed for orchestrator registrations and source generation.
/// Activities and entities ignore this attribute in v1.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DurableTaskVersionAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DurableTaskVersionAttribute"/> class.
    /// </summary>
    /// <param name="version">The version string for the orchestrator.</param>
    public DurableTaskVersionAttribute(string? version = null)
    {
        this.Version = string.IsNullOrEmpty(version) ? default : new TaskVersion(version!);
    }

    /// <summary>
    /// Gets the orchestrator version declared on the attributed class.
    /// </summary>
    public TaskVersion Version { get; }
}
