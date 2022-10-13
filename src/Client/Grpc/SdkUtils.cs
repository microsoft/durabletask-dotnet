// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask;

/// <summary>
/// Common utilities and helpers.
/// </summary>
static class SdkUtils
{
    /// <summary>
    /// Gets a logger from the factory.
    /// </summary>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <returns>A created logger.</returns>
    internal static ILogger GetLogger(ILoggerFactory loggerFactory)
        => loggerFactory.CreateLogger("Microsoft.DurableTask");

    /// <summary>
    /// Gets the host of the Durable Task sidecar, which is responsible for managing and scheduling durable tasks.
    /// </summary>
    /// <param name="configuration">Optional configuration provider for looking up the sidecar address.</param>
    /// <returns>
    /// Returns the configured sidecar host, or "127.0.0.1" if no explicit value is configured.
    /// </returns>
    internal static string GetSidecarHost(IConfiguration? configuration)
    {
        string host = GetSetting(EnvironmentVariables.SidecarHost, configuration, "127.0.0.1");
        return host;
    }

    /// <summary>
    /// Gets tha port of the Durable Task sidecar, which is responsible for managing and scheduling durable tasks.
    /// </summary>
    /// <param name="configuration">Optional configuration provider for looking up the sidecar address.</param>
    /// <returns>
    /// Returns the configured sidecar port, or "4001" if no explicit value is configured.
    /// </returns>
    internal static int GetSidecarPort(IConfiguration? configuration)
    {
        string portSetting = GetSetting(EnvironmentVariables.SidecarPort, configuration, "4001");
        if (!int.TryParse(portSetting, out int port))
        {
            throw new InvalidOperationException($"The value '{portSetting}' is not a valid port number.");
        }

        return port;
    }

    static string GetSetting(string name, IConfiguration? configuration, string defaultValue)
    {
        return configuration?[name] ?? Environment.GetEnvironmentVariable(name) ?? defaultValue;
    }

    /// <summary>
    /// An empty service provider implementation.
    /// </summary>
    internal class EmptyServiceProvider : IServiceProvider
    {
        /// <summary>
        /// Gets the singleton instance.
        /// </summary>
        public static EmptyServiceProvider Instance { get; } = new();

        /// <inheritdoc/>
        public object? GetService(Type serviceType) => null;
    }

    static class EnvironmentVariables
    {
        // All environment variables should be prefixed with DURABLETASK_
        public const string SidecarHost = "DURABLETASK_SIDECAR_HOST";
        public const string SidecarPort = "DURABLETASK_SIDECAR_PORT";
    }
}
