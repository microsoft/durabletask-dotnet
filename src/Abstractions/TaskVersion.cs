// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// The version of a durable task.
/// </summary>
public readonly struct TaskVersion : IEquatable<TaskVersion>
{
    /// <summary>
    /// A sentinel value representing an unversioned task. Equivalent to <c>default(TaskVersion)</c> and
    /// <c>new TaskVersion(string.Empty)</c>.
    /// </summary>
    /// <remarks>
    /// Use this on <see cref="TaskOptions.Version"/> to explicitly request the unversioned task
    /// implementation from a versioned orchestration. <c>null</c> on the same property means the activity
    /// inherits the orchestration instance version.
    /// </remarks>
    public static readonly TaskVersion Unversioned = default;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskVersion"/> struct.
    /// </summary>
    /// <param name="version">The version of the task. <c>null</c> or <see cref="string.Empty"/> produces
    /// an unversioned <see cref="TaskVersion"/> equal to <see cref="Unversioned"/>.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="version"/> is non-empty but contains only whitespace. Pass <c>null</c>,
    /// <see cref="string.Empty"/>, or use <see cref="Unversioned"/> to represent an unversioned task.
    /// </exception>
    public TaskVersion(string version)
    {
        // Normalize null/empty to string.Empty so default(TaskVersion), TaskVersion.Unversioned, and
        // new TaskVersion("") all compare and hash identically. Without this normalization the struct's
        // Version field can be null, which makes Equals(null, "") return false and causes
        // StringComparer.OrdinalIgnoreCase.GetHashCode to throw at runtime when the struct is used as a
        // dictionary key.
        if (string.IsNullOrEmpty(version))
        {
            this.Version = string.Empty;
            return;
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException(
                "Version must not be whitespace-only. Pass null, an empty string, or use TaskVersion.Unversioned to represent an unversioned task.",
                nameof(version));
        }

        this.Version = version;
    }

    /// <summary>
    /// Gets the version of a task. Returns <see cref="string.Empty"/> for an unversioned task; never
    /// returns <c>null</c>.
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
        // Treat null and empty Version as the same unversioned identity. Combined with normalization in
        // the constructor, both default(TaskVersion) and new TaskVersion("") compare equal and hash to
        // the same value as TaskVersion.Unversioned.
        string left = this.Version ?? string.Empty;
        string right = other.Version ?? string.Empty;
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
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
        // Null-safe: a default-constructed TaskVersion (or one created via the implicit conversion from
        // null) must not crash when used as a dictionary key. Treats null and empty as the same key.
        string value = this.Version ?? string.Empty;
        return StringComparer.OrdinalIgnoreCase.GetHashCode(value);
    }
}
