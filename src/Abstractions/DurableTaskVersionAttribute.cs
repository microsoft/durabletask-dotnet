// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Indicates the version of a class-based durable orchestrator or activity.
/// </summary>
/// <remarks>
/// This attribute is consumed for orchestrator and activity registrations and source generation where applicable.
/// Entities ignore this attribute.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DurableTaskVersionAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DurableTaskVersionAttribute"/> class.
    /// </summary>
    /// <param name="version">The version string for the orchestrator or activity.</param>
    public DurableTaskVersionAttribute(string? version = null)
    {
        if (version is not null && version.Length > 0 && string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException(
                "Version must not be whitespace-only. Provide a non-empty version string or omit the attribute argument to declare an unversioned task.",
                nameof(version));
        }

        this.Version = string.IsNullOrEmpty(version) ? default : new TaskVersion(version!);
    }

    /// <summary>
    /// Gets the durable task version declared on the attributed class.
    /// </summary>
    public TaskVersion Version { get; }
}
