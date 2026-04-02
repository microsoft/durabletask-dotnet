// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Represents the logical name and version of a registered orchestrator.
/// </summary>
internal readonly struct OrchestratorVersionKey : IEquatable<OrchestratorVersionKey>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestratorVersionKey"/> struct.
    /// </summary>
    /// <param name="name">The orchestrator name.</param>
    /// <param name="version">The orchestrator version.</param>
    public OrchestratorVersionKey(TaskName name, TaskVersion version)
        : this(name.Name, version.Version)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestratorVersionKey"/> struct.
    /// </summary>
    /// <param name="name">The orchestrator name.</param>
    /// <param name="version">The orchestrator version.</param>
    public OrchestratorVersionKey(string name, string? version)
    {
        this.Name = Check.NotNullOrEmpty(name, nameof(name));
        this.Version = version ?? string.Empty;
    }

    /// <summary>
    /// Gets the logical orchestrator name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the orchestrator version.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Determines whether the specified key is equal to the current key.
    /// </summary>
    /// <param name="other">The key to compare with the current key.</param>
    /// <returns><c>true</c> if the keys are equal; otherwise <c>false</c>.</returns>
    public bool Equals(OrchestratorVersionKey other)
    {
        return string.Equals(this.Name, other.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(this.Version, other.Version, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is OrchestratorVersionKey other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        unchecked
        {
            return (StringComparer.OrdinalIgnoreCase.GetHashCode(this.Name) * 397)
                ^ StringComparer.OrdinalIgnoreCase.GetHashCode(this.Version);
        }
    }
}
