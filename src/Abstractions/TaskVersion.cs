// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// The version of a durable task.
/// </summary>
public readonly struct TaskVersion : IEquatable<TaskVersion>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TaskVersion"/> struct.
    /// </summary>
    /// <param name="version">The version of the task. Providing <c>null</c> will result in the default struct.</param>
    public TaskVersion(string version)
    {
        if (version == null)
        {
            this.Version = null!;
        }
        else
        {
            this.Version = version;
        }
    }

    /// <summary>
    /// Gets the version of a task.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Implicitly converts a <see cref="TaskVersion"/> into a <see cref="string"/> of the <see cref="Version"/> property value.
    /// </summary>
    /// <param name="value">The <see cref="TaskVersion"/> to be converted into a <see cref="string"/>.</param>
    public static implicit operator string(TaskVersion value) => value.Version;

    /// <summary>
    /// Implicitly converts a <see cref="string"/> into a <see cref="TaskVersion"/>.
    /// </summary>
    /// <param name="value">The <see cref="string"/> to convert into a <see cref="TaskVersion"/>.</param>
    public static implicit operator TaskVersion(string value) => new TaskVersion(value);

    /// <summary>
    /// Compares two <see cref="TaskVersion"/> structs for equality.
    /// </summary>
    /// <param name="a">The first <see cref="TaskVersion"/> to compare.</param>
    /// <param name="b">The second <see cref="TaskVersion"/> to compare.</param>
    /// <returns><c>true</c> if the two <see cref="TaskVersion"/> objects are equal; otherwise <c>false</c>.</returns>
    public static bool operator ==(TaskVersion a, TaskVersion b)
    {
        return a.Equals(b);
    }

    /// <summary>
    /// Compares two <see cref="TaskVersion"/> structs for inequality.
    /// </summary>
    /// <param name="a">The first <see cref="TaskVersion"/> to compare.</param>
    /// <param name="b">The second <see cref="TaskVersion"/> to compare.</param>
    /// <returns><c>false</c> if the two <see cref="TaskVersion"/> objects are equal; otherwise <c>true</c>.</returns>
    public static bool operator !=(TaskVersion a, TaskVersion b)
    {
        return !a.Equals(b);
    }

    /// <summary>
    /// Gets a value indicating whether to <see cref="TaskVersion"/> objects
    /// are equal using value semantics.
    /// </summary>
    /// <param name="other">The other <see cref="TaskVersion"/> to compare to.</param>
    /// <returns><c>true</c> if the two <see cref="TaskVersion"/> are equal using value semantics; otherwise <c>false</c>.</returns>
    public bool Equals(TaskVersion other)
    {
        return string.Equals(this.Version, other.Version, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets a value indicating whether to <see cref="TaskVersion"/> objects
    /// are equal using value semantics.
    /// </summary>
    /// <param name="obj">The other object to compare to.</param>
    /// <returns><c>true</c> if the two objects are equal using value semantics; otherwise <c>false</c>.</returns>
    public override bool Equals(object? obj)
    {
        if (obj is not TaskVersion other)
        {
            return false;
        }

        return this.Equals(other);
    }

    /// <summary>
    /// Calculates a hash code value for the current <see cref="TaskVersion"/> instance.
    /// </summary>
    /// <returns>A 32-bit hash code value.</returns>
    public override int GetHashCode()
    {
        return StringComparer.OrdinalIgnoreCase.GetHashCode(this.Version);
    }
}
