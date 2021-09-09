//  ----------------------------------------------------------------------------------
//  Copyright Microsoft Corporation
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//  http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//  ----------------------------------------------------------------------------------

using System;

namespace DurableTask;

/// <summary>
/// The name of a durable task.
/// </summary>
public struct TaskName : IEquatable<TaskName>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TaskName"/> struct.
    /// </summary>
    /// <param name="name">The name of the task.</param>
    /// <param name="version">The version of the task, if applicable.</param>
    public TaskName(string name, string? version = null)
    {
        this.Name = name ?? throw new ArgumentNullException(nameof(name));
        this.Version = version ?? "";
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
    public string Version { get; }

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
    /// Implicitly converts a <see cref="TaskName"/> into a <see cref="string"/> of the <see cref="Name"/> property value.
    /// </summary>
    /// <param name="value">The <see cref="TaskName"/> to be converted into a string.</param>
    public static implicit operator string(TaskName value) => value.Name;

    /// <summary>
    /// Implicitly converts a <see cref="string"/> into a <see cref="TaskName"/> value.
    /// </summary>
    /// <param name="value">The string to convert into a <see cref="TaskName"/>.</param>
    public static implicit operator TaskName(string value) => new(value);

    /// <summary>
    /// Gets a value indicating whether to <see cref="TaskName"/> objects
    /// are equal using value semantics.
    /// </summary>
    /// <param name="other">The other object to compare to.</param>
    /// <returns><c>true</c> if the two objects are equal using value semantics; otherwise <c>false</c>.</returns>
    public bool Equals(TaskName other)
    {
        return string.Equals(this.Name, other.Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets a value indicating whether to <see cref="TaskName"/> objects
    /// are equal using value semantics.
    /// </summary>
    /// <param name="other">The other object to compare to.</param>
    /// <returns><c>true</c> if the two objects are equal using value semantics; otherwise <c>false</c>.</returns>
    public override bool Equals(object? other)
    {
        if (other is not TaskName)
        {
            return false;
        }

        return this.Equals((TaskName)other);
    }

    /// <summary>
    /// Calculates a hash code value for the current <see cref="TaskName"/> instance.
    /// </summary>
    /// <returns>A 32-bit hash code value.</returns>
    public override int GetHashCode()
    {
        return StringComparer.OrdinalIgnoreCase.GetHashCode(this.Name);
    }

    /// <summary>
    /// Gets the string value of the current <see cref="TaskName"/> instance.
    /// </summary>
    /// <returns>The name and optional version of the current <see cref="TaskName"/> instance.</returns>
    public override string ToString()
    {
        return this.Name;
    }
}
