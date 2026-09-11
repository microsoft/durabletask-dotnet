// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Extensions for <see cref="Type" />.
/// </summary>
static class TypeExtensions
{
    /// <summary>
    /// Gets the task name for a type.
    /// </summary>
    /// <param name="type">The type to get a task name for.</param>
    /// <returns>The task name.</returns>
    public static TaskName GetTaskName(this Type type)
    {
        // IMPORTANT: This logic needs to be kept consistent with the source generator logic.
        Check.NotNull(type);
        return Attribute.GetCustomAttribute(type, typeof(DurableTaskAttribute)) switch
        {
            DurableTaskAttribute { Name.Name: not null and not "" } attr => attr.Name,
            _ => new TaskName(type.Name),
        };
    }

    /// <summary>
    /// Gets the durable task version for a type.
    /// </summary>
    /// <param name="type">The type to get the durable task version for.</param>
    /// <returns>The durable task version.</returns>
    /// <remarks>
    /// When the <see cref="DurableTaskAttribute.Version"/> declares multiple comma-separated versions, this
    /// returns only the first declared version. Prefer <see cref="GetDurableTaskVersions"/> when all declared
    /// versions are needed (for example, when registering a type under every version it supports).
    /// </remarks>
    internal static TaskVersion GetDurableTaskVersion(this Type type)
    {
        // IMPORTANT: This logic needs to be kept consistent with the source generator logic.
        Check.NotNull(type);
        IReadOnlyList<TaskVersion> versions = type.GetDurableTaskVersions();
        return versions.Count > 0 ? versions[0] : default;
    }

    /// <summary>
    /// Gets every durable task version declared for a type via <see cref="DurableTaskAttribute.Version"/>.
    /// </summary>
    /// <param name="type">The type to get the durable task versions for.</param>
    /// <returns>
    /// The distinct (case-insensitive) set of declared versions in declaration order. An unversioned type
    /// (no attribute, or an empty/unset <see cref="DurableTaskAttribute.Version"/>) yields a single
    /// <see cref="TaskVersion.Unversioned"/> entry so callers can always register at least once.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when any comma-separated entry is whitespace-only. This mirrors the source generator's
    /// <c>DURABLE3005</c> diagnostic so the reflection-based registration path fails closed for types whose
    /// attribute the generator did not see.
    /// </exception>
    internal static IReadOnlyList<TaskVersion> GetDurableTaskVersions(this Type type)
    {
        // IMPORTANT: This logic needs to be kept consistent with the source generator logic.
        Check.NotNull(type);
        if (Attribute.GetCustomAttribute(type, typeof(DurableTaskAttribute)) is not DurableTaskAttribute attr
            || string.IsNullOrEmpty(attr.Version))
        {
            return new[] { TaskVersion.Unversioned };
        }

        List<TaskVersion> versions = new();
        foreach (string segment in attr.Version!.Split(','))
        {
            if (segment.Length == 0)
            {
                // Truly-empty entry (e.g. a trailing or doubled comma). Skip silently.
                continue;
            }

            string trimmed = segment.Trim();
            if (trimmed.Length == 0)
            {
                // Whitespace-only entry. Fail closed, consistent with the TaskVersion constructor and the
                // source generator's DURABLE3005 diagnostic.
                throw new ArgumentException(
                    "A [DurableTask] Version entry must not be whitespace-only. Provide non-empty version " +
                    "values or omit the Version argument to declare an unversioned task.",
                    nameof(type));
            }

            TaskVersion version = new(trimmed);
            if (!versions.Contains(version))
            {
                versions.Add(version);
            }
        }

        return versions.Count > 0 ? versions : new[] { TaskVersion.Unversioned };
    }
}
