// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Default implementation of <see cref="IDurableTaskClientFactory" />.
/// </summary>
class DefaultDurableTaskClientFactory : IDurableTaskClientFactory
{
    readonly IServiceProvider serviceProvider;
    readonly ILoggerFactory loggerFactory;
    readonly ClientFactoryConfiguration? configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultDurableTaskClientFactory"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="configuration">The factory configuration, if available.</param>
    public DefaultDurableTaskClientFactory(
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory,
        ClientFactoryConfiguration? configuration = null)
    {
        this.serviceProvider = Check.NotNull(serviceProvider);
        this.loggerFactory = Check.NotNull(loggerFactory);
        this.configuration = configuration;
    }

    /// <inheritdoc/>
    public DurableTaskClient CreateClient(string? name = null)
    {
        name ??= Options.DefaultName;
        return this.CreateClientCore(name, optionsType: null, configureOptions: null);
    }

    /// <inheritdoc/>
    public DurableTaskClient CreateClient<TOptions>(string? name, Action<TOptions> configureOptions)
        where TOptions : DurableTaskClientOptions, new()
    {
        Check.NotNull(configureOptions);
        name ??= Options.DefaultName;
        return this.CreateClientCore(name, typeof(TOptions), configureOptions);
    }

    static void CopyProperties(object source, object target, Type type)
    {
        foreach (System.Reflection.PropertyInfo property in type.GetProperties())
        {
            if (property.CanRead && property.CanWrite)
            {
                object? value = property.GetValue(source);
                property.SetValue(target, value);
            }
        }
    }

    DurableTaskClient CreateClientCore(string name, Type? optionsType, object? configureOptions)
    {
        if (this.configuration is null)
        {
            throw new InvalidOperationException(
                "The DurableTaskClient factory has not been configured. " +
                "Ensure that a client type has been registered (e.g., by calling UseGrpc()) " +
                "when configuring the DurableTaskClient.");
        }

        Type clientType = this.configuration.ClientType;
        Type resolvedOptionsType = optionsType ?? this.configuration.OptionsType;

        // Get the base options from the named options
        object options = this.GetConfiguredOptions(name, resolvedOptionsType);

        // Apply any custom configuration
        if (configureOptions is not null)
        {
            // Use reflection to invoke the action on the options
            Type actionType = typeof(Action<>).MakeGenericType(resolvedOptionsType);
            actionType.GetMethod("Invoke")!.Invoke(configureOptions, new[] { options });
        }

        // Create the client using ActivatorUtilities
        ILogger logger = this.loggerFactory.CreateLogger(clientType);

        // The client constructor expects: (string name, TOptions options, ILogger logger)
        return (DurableTaskClient)Activator.CreateInstance(clientType, name, options, logger)!;
    }

    object GetConfiguredOptions(string name, Type optionsType)
    {
        // Create a new instance of the options
        object options = Activator.CreateInstance(optionsType)!;

        // Try to get the IOptionsMonitor for the specific options type and apply configuration
        Type optionsMonitorType = typeof(IOptionsMonitor<>).MakeGenericType(optionsType);
        object? optionsMonitor = this.serviceProvider.GetService(optionsMonitorType);

        if (optionsMonitor is not null)
        {
            // Call optionsMonitor.Get(name) to get the configured options
            object? configuredOptions = optionsMonitorType.GetMethod("Get")!.Invoke(optionsMonitor, new object[] { name });
            if (configuredOptions is not null)
            {
                // Copy properties from configured options to our new instance
                CopyProperties(configuredOptions, options, optionsType);
            }
        }

        return options;
    }

    /// <summary>
    /// Configuration for the client factory.
    /// </summary>
    internal sealed class ClientFactoryConfiguration
    {
        /// <summary>
        /// Gets or sets the type of client to create.
        /// </summary>
        public Type ClientType { get; set; } = null!;

        /// <summary>
        /// Gets or sets the type of options for the client.
        /// </summary>
        public Type OptionsType { get; set; } = typeof(DurableTaskClientOptions);
    }
}
