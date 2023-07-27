// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Entities;

/// <summary>
/// Represents the ID of an entity.
/// </summary>
/// <param name="Name">The name of the entity.</param>
/// <param name="Key">The key for this entity.</param>
public readonly record struct EntityInstanceId(string Name, string Key)
{
    /// <inheritdoc/>
    public override string ToString() => $"@{this.Name}@{this.Key}";
}
