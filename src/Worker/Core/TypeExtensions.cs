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
        // IMPORTANT: This logic needs to be kept consistent with the source generator logic
        Check.NotNull(type);
        if (Attribute.GetCustomAttribute(type, typeof(DurableTaskAttribute))
            is DurableTaskAttribute attribute)
        {
            return attribute.Name;
        }
        else
        {
            return type.Name;
        }
    }
}
