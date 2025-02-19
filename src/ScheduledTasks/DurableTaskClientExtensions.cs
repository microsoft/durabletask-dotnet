// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Extension methods for working with <see cref="DurableTaskClient" />.
/// </summary>
public static class DurableTaskClientExtensions
{
    /// <summary>
    /// Gets a client for working with scheduled tasks.
    /// </summary>
    /// <param name="client">The DurableTaskClient instance.</param>
    /// <returns>A client for managing scheduled tasks.</returns>
    public static ScheduledTaskClient ScheduledTasks(this DurableTaskClient client)
    {
        return new ScheduledTaskClient(client);
    }
}
