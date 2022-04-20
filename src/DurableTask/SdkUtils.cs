// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask;

static class SdkUtils
{
    internal static readonly IServiceProvider EmptyServiceProvider = new ServiceCollection().BuildServiceProvider();
    internal static readonly IDataConverter DefaultDataConverter = JsonDataConverter.Default;

    /// <summary>
    /// Helper for validating addresses passed to the <see cref="DurableTaskGrpcWorker"/> and <see cref="DurableTaskGrpcClient"/> types.
    /// </summary>
    /// <param name="address">Expected to be an HTTP address, like http://localhost:4000.</param>
    /// <returns>Returns the unmodified input as a convenience.</returns>
    internal static string ValidateAddress(string address)
    {
        if (string.IsNullOrEmpty(address))
        {
            ArgumentNullException.ThrowIfNull(address, nameof(address));
        }
        else if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri))
        {
            throw new ArgumentException("The address must be a well-formed URI.", nameof(address));
        }
        else if (!uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The address must be an HTTP or HTTPS address.", nameof(address));
        }

        return address;
    }

    internal static ILogger GetLogger(ILoggerFactory loggerFactory) => loggerFactory.CreateLogger("Microsoft.DurableTask");

    /// <summary>
    /// Gets the address of the Durable Task sidecar, which is responsible for managing and scheduling durable tasks.
    /// </summary>
    /// <param name="configuration">Optional configuration provider for looking up the sidecar address.</param>
    /// <returns>Returns the configured sidecar address, or http://localhost:4001 if no explicit value is configured.</returns>
    internal static string GetSidecarAddress(IConfiguration? configuration)
    {
        string address = GetSetting(EnvironmentVariables.SidecarAddress, configuration, "http://localhost:4001");
        return ValidateAddress(address);
    }

    static string GetSetting(string name, IConfiguration? configuration, string defaultValue)
    {
        return configuration?[name] ?? Environment.GetEnvironmentVariable(name) ?? defaultValue;
    }

    static class EnvironmentVariables
    {
        // All environment variables should be prefixed with DURABLETASK_
        public const string SidecarAddress = "DURABLETASK_SIDECAR_ADDRESS";
    }
}
