// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Extension methods for configuring Durable Task clients to use scheduled tasks.
/// </summary>
public static class DurableTaskClientBuilderExtensions
{
    /// <summary>
    /// Enables scheduled tasks support for the client builder.
    /// </summary>
    /// <param name="builder">The client builder to add scheduled task support to.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskClientBuilder UseScheduledTasks(this IDurableTaskClientBuilder builder)
    {
        builder.Services.AddTransient<ScheduledTaskClient>(sp =>
        {
            DurableTaskClient client = sp.GetRequiredService<DurableTaskClient>();
            ILogger<DefaultScheduledTaskClient> logger = sp.GetRequiredService<ILogger<DefaultScheduledTaskClient>>();
            return new DefaultScheduledTaskClient(client, logger);
        });

        return builder;
    }
}
