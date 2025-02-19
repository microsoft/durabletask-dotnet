// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

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
    /// <param name="logger">logger for ScheduledTaskClient.</param>
    /// <returns>A client for managing scheduled tasks.</returns>
    public static ScheduledTaskClient ScheduledTasks(this DurableTaskClient client, ILogger logger)
    {
        // ScheduledTaskClient is not a resource-intensive object, we shall create a new instance to avoid
        // any potential thread safety issues.
        return new ScheduledTaskClient(client, logger);
    }
}
