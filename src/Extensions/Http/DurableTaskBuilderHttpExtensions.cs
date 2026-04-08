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
    /// <see cref="System.Net.Http.HttpClient"/> to execute HTTP requests. This extension also registers
    /// and uses a named <see cref="IHttpClientFactory"/> client named <c>"DurableHttp"</c>.
    /// </para>
    /// <para>
    /// The built-in activity resolves <see cref="IHttpClientFactory"/> from the service provider and
    /// creates the <c>"DurableHttp"</c> client at runtime. Removing or overriding that DI registration
    /// in a way that makes <see cref="IHttpClientFactory"/> unavailable will cause this extension to fail.
    /// </para>
    /// <para>
    /// Calling this method multiple times is safe — the second and subsequent calls are no-ops.
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
            try
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
            }
            catch (ArgumentException)
            {
                // Activity already registered (e.g. UseHttpActivities() called twice or user
                // registered their own "BuiltIn::HttpActivity"). This is a safe no-op.
            }
        });

        return builder;
    }
}
