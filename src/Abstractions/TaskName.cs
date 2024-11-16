// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// The name of a durable task.
/// </summary>
public readonly struct TaskName : IEquatable<TaskName>
{
    // TODO: Add detailed remarks that describe the role of TaskName

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskName"/> struct.
    /// </summary>
    /// <param name="name">The name of the task. Providing <c>null</c> will yield the default struct.</param>
    public TaskName(string name)
    {
        if (name is null)
        {
            // Force the default struct when null is passed in.
            this.Name = null!;
            this.Version = null!;
        }
        else
        {
            this.Name = name;
            this.Version = string.Empty; // expose setting Version only when we actually consume it.
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskName"/> struct.
    /// </summary>
    /// <param name="name">The name of the task. Providing <c>null</c> will yield the default struct.</param>
    /// <param name="version">The version of the task.</param>
    public TaskName(string name, string version)
    {
        if (name is null)
        {
            if (version is null)
            {
                // Force the default struct when null is passed in.
                this.Name = null!;
                this.Version = null!;
            }
            else
            {
                throw new ArgumentException("name must not be null when version is non-null");
            }
        }
        else
        {
            if (version is null)
            {
                this.Name = name;
                this.Version = string.Empty; // fallback to the constructor without version parameter
            }
            else
            {
                this.Name = name;
                this.Version = version;
            }
        }
    }

    /// <summary>
    /// Gets the name of the task without the version.
    /// </summary>
    /// <value>
    /// The name of the activity task without the version.
    /// </value>
    public string Name { get; }

    /// <summary>
    /// Gets the version of the task.
    /// </summary>
    /// <value>
    /// The version of the activity task.
    /// </value>
    public string Version { get; }

    /// <summary>
    /// Implicitly converts a <see cref="TaskName"/> into a <see cref="string"/> of the <see cref="Name"/> property value.
    /// </summary>
    /// <param name="value">The <see cref="TaskName"/> to be converted into a string.</param>
    public static implicit operator string(TaskName value) => value.ToString();

    /// <summary>
    /// Implicitly converts a <see cref="string"/> into a <see cref="TaskName"/> value.
    /// </summary>
    /// <param name="value">The string to convert into a <see cref="TaskName"/>.</param>
    public static implicit operator TaskName(string value) => FromString(value);

    /// <summary>
    /// Compares two <see cref="TaskName"/> objects for equality.
    /// </summary>
    /// <param name="a">The first <see cref="TaskName"/> to compare.</param>
    /// <param name="b">The second <see cref="TaskName"/> to compare.</param>
    /// <returns><c>true</c> if the two <see cref="TaskName"/> objects are equal; otherwise <c>false</c>.</returns>
    public static bool operator ==(TaskName a, TaskName b)
    {
        return a.Equals(b);
    }

    /// <summary>
    /// Compares two <see cref="TaskName"/> objects for inequality.
    /// </summary>
    /// <param name="a">The first <see cref="TaskName"/> to compare.</param>
    /// <param name="b">The second <see cref="TaskName"/> to compare.</param>
    /// <returns><c>true</c> if the two <see cref="TaskName"/> objects are not equal; otherwise <c>false</c>.</returns>
    public static bool operator !=(TaskName a, TaskName b)
    {
        return !a.Equals(b);
    }

    /// <summary>
    /// Parses the taskname string and initializes a new instance of the <see cref="TaskName"/> struct.
    /// </summary>
    /// <param name="taskname">The taskname string in format of "Name:Version" or "Name".</param>
    /// <returns>New <see cref="TaskName"/> instance parsed from taskname string.</returns>
    public static TaskName FromString(string taskname)
    {
        if (string.IsNullOrEmpty(taskname))
        {
            return default;
        }

        string[] parts = taskname.Split(':');
        if (parts.Length == 1)
        {
            return new TaskName(parts[0]);
        }

        if (parts.Length == 2)
        {
            return new TaskName(parts[0], parts[1]);
        }

        throw new ArgumentException("Invalid task name format: taskname=" + taskname);
    }

    /// <summary>
    /// Gets a value indicating whether to <see cref="TaskName"/> objects
    /// are equal using value semantics.
    /// </summary>
    /// <param name="other">The other object to compare to.</param>
    /// <returns><c>true</c> if the two objects are equal using value semantics; otherwise <c>false</c>.</returns>
    public bool Equals(TaskName other)
    {
        return string.Equals(this.Name, other.Name, StringComparison.OrdinalIgnoreCase)
          && string.Equals(this.Version, other.Version, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets a value indicating whether to <see cref="TaskName"/> objects
    /// are equal using value semantics.
    /// </summary>
    /// <param name="obj">The other object to compare to.</param>
    /// <returns><c>true</c> if the two objects are equal using value semantics; otherwise <c>false</c>.</returns>
    public override bool Equals(object? obj)
    {
        if (obj is not TaskName other)
        {
            return false;
        }

        return this.Equals(other);
    }

    /// <summary>
    /// Calculates a hash code value for the current <see cref="TaskName"/> instance.
    /// </summary>
    /// <returns>A 32-bit hash code value.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(this.Name, this.Version);
    }

    /// <summary>
    /// Gets the string value of the current <see cref="TaskName"/> instance.
    /// </summary>
    /// <returns>The name and optional version of the current <see cref="TaskName"/> instance.</returns>
    public override string ToString()
    {
        if (string.IsNullOrEmpty(this.Version))
        {
            return this.Name;
        }
        else
        {
            return this.Name + ":" + this.Version;
        }
    }
}
