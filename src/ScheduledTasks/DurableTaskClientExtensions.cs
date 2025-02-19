// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;

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
        // ScheduledTaskClient is not a resource-intensive object, we shall create a new instance to avoid
        // any potential thread safety issues.
        return new ScheduledTaskClient(client);
    }
}
