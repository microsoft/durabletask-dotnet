// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Entities;

/// <summary>
/// Represents the ID of an entity.
/// </summary>
public readonly struct EntityInstanceId
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EntityInstanceId"/> struct.
    /// </summary>
    /// <param name="name">The name of the entity.</param>
    /// <param name="key">The key for this entity.</param>
    public EntityInstanceId(string name, string key)
    {
        this.Name = Check.NotNullOrEmpty(name);
        this.Key = Check.NotNullOrEmpty(key);
    }

    /// <summary>
    /// Gets the name of this entity instance ID.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the key of this entity instance ID.
    /// </summary>
    public string Key { get; }

    /// <inheritdoc/>
    public override string ToString() => $"@{this.Name}@{this.Key}";
}
