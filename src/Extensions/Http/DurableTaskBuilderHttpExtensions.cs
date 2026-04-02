// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Http;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask;

/// <summary>
/// Extension methods for adding built-in HTTP activity support to a Durable Task worker.
/// </summary>
public static class DurableTaskBuilderHttpExtensions
{
    /// <summary>
    /// The well-known activity name for the built-in HTTP activity.
    /// </summary>
    public const string HttpTaskActivityName = "BuiltIn::HttpActivity";

    /// <summary>
    /// Adds the built-in HTTP activity to the worker, enabling
    /// <see cref="TaskOrchestrationContextHttpExtensions.CallHttpAsync(TaskOrchestrationContext, DurableHttpRequest)"/>
    /// in standalone (non-Azure Functions) scenarios.
    /// </summary>
    /// <param name="builder">The worker builder.</param>
    /// <returns>The builder, for call chaining.</returns>
    /// <remarks>
    /// <para>
    /// This registers an internal activity named <c>"BuiltIn::HttpActivity"</c> that uses
    /// <see cref="System.Net.Http.HttpClient"/> to execute HTTP requests. If an <c>IHttpClientFactory</c>
    /// is registered in the service collection, a named client <c>"DurableHttp"</c> is used.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// builder.Services.AddDurableTaskWorker()
    ///     .AddTasks(registry => { /* your activities */ })
    ///     .UseHttpActivities()
    ///     .UseGrpc();
    /// </code>
    /// </para>
    /// </remarks>
    public static IDurableTaskWorkerBuilder UseHttpActivities(this IDurableTaskWorkerBuilder builder)
    {
        Check.NotNull(builder);

        builder.Services.AddHttpClient("DurableHttp");

        builder.AddTasks(registry =>
        {
            registry.AddActivity(
                new TaskName(HttpTaskActivityName),
                sp =>
                {
                    IHttpClientFactory httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                    HttpClient client = httpClientFactory.CreateClient("DurableHttp");
                    ILogger logger = sp.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Microsoft.DurableTask.Http.BuiltInHttpActivity");
                    return new BuiltInHttpActivity(client, logger);
                });
        });

        return builder;
    }
}
