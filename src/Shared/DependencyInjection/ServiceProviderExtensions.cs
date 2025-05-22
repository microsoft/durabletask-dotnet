// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Dapr.DurableTask;

/// <summary>
/// Extensions for <see cref="IServiceProvider" />.
/// </summary>
static class ServiceProviderExtensions
{
    /// <summary>
    /// Gets the current options from the service provider.
    /// </summary>
    /// <typeparam name="TOptions">The option type to get.</typeparam>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="name">The name of the options. <c>null</c> for default name.</param>
    /// <returns>The options, if available. <c>null</c> otherwise.</returns>
    public static TOptions GetOptions<TOptions>(
        this IServiceProvider serviceProvider, string? name = null)
        where TOptions : class
    {
        Check.NotNull(serviceProvider);
        IOptionsMonitor<TOptions>? options = serviceProvider.GetRequiredService<IOptionsMonitor<TOptions>>();
        return options.Get(name);
    }
}
