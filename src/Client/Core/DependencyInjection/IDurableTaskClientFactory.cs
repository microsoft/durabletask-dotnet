// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client;

/// <summary>
/// A factory for dynamically creating <see cref="DurableTaskClient" /> instances.
/// </summary>
/// <remarks>
/// <para>
/// This factory allows for dynamic creation of <see cref="DurableTaskClient" /> instances at runtime,
/// which is useful when the task hub name or other client configuration needs to be determined dynamically
/// (e.g., from a route parameter in an HTTP request).
/// </para>
/// <para>
/// Unlike <see cref="IDurableTaskClientProvider" /> which retrieves pre-registered clients by name,
/// this factory creates new client instances based on the provided options.
/// </para>
/// <para>
/// Clients created by this factory should be disposed when no longer needed, or cached appropriately
/// to avoid creating multiple connections to the same task hub.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class WebhookHttpTrigger
/// {
///     private readonly IDurableTaskClientFactory clientFactory;
///
///     public WebhookHttpTrigger(IDurableTaskClientFactory clientFactory)
///     {
///         this.clientFactory = clientFactory;
///     }
///
///     public async Task RunAsync(string taskHubName, string orchestrationId)
///     {
///         // Create a client for the specified task hub dynamically
///         await using DurableTaskClient client = this.clientFactory.CreateClient(taskHubName);
///         await client.RaiseEventAsync(orchestrationId, "MyEvent", eventData);
///     }
/// }
/// </code>
/// </example>
public interface IDurableTaskClientFactory
{
    /// <summary>
    /// Creates a new <see cref="DurableTaskClient" /> with the specified name using the default configuration.
    /// </summary>
    /// <param name="name">
    /// The name of the client to create. This is typically used to look up configuration options
    /// from a named options pattern. If <c>null</c>, the default client configuration is used.
    /// </param>
    /// <returns>A new <see cref="DurableTaskClient" /> instance.</returns>
    /// <remarks>
    /// The caller is responsible for disposing of the returned client when it is no longer needed.
    /// </remarks>
    DurableTaskClient CreateClient(string? name = null);

    /// <summary>
    /// Creates a new <see cref="DurableTaskClient" /> with custom configuration.
    /// </summary>
    /// <typeparam name="TOptions">The type of options used to configure the client.</typeparam>
    /// <param name="name">The name of the client to create.</param>
    /// <param name="configureOptions">An action to configure the client options.</param>
    /// <returns>A new <see cref="DurableTaskClient" /> instance.</returns>
    /// <remarks>
    /// <para>
    /// This method allows for customizing the client options before creation. The options are first
    /// populated from the named options (if available), then the <paramref name="configureOptions"/>
    /// action is applied to further customize them.
    /// </para>
    /// <para>
    /// The caller is responsible for disposing of the returned client when it is no longer needed.
    /// </para>
    /// </remarks>
    DurableTaskClient CreateClient<TOptions>(string? name, Action<TOptions> configureOptions)
        where TOptions : DurableTaskClientOptions, new();
}
