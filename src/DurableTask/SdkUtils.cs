// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Grpc.Core;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask;

static class SdkUtils
{
    internal static readonly IServiceProvider EmptyServiceProvider = new ServiceCollection().BuildServiceProvider();
    internal static readonly DataConverter DefaultDataConverter = JsonDataConverter.Default;

    /// <summary>
    /// Helper for validating addresses passed to the <see cref="DurableTaskGrpcWorker"/> and <see cref="DurableTaskGrpcClient"/> types.
    /// </summary>
    /// <param name="address">Expected to be an HTTP address, like http://localhost:4000.</param>
    /// <returns>Returns the unmodified input as a convenience.</returns>
    internal static string ValidateAddress(string address)
    {
        if (string.IsNullOrEmpty(address))
        {
            throw new ArgumentException("The address value cannot be null or empty", nameof(address));
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
    internal static string GetSidecarHost(IConfiguration? configuration)
    {
        string host = GetSetting(EnvironmentVariables.SidecarHost, configuration, "127.0.0.1");
        return host;
    }

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

    internal static async IAsyncEnumerable<T> ReadAllAsync<T>(
        this IAsyncStreamReader<T> reader,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (reader == null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        while (await reader.MoveNext(cancellationToken).ConfigureAwait(false))
        {
            yield return reader.Current;
        }
    }

    static class EnvironmentVariables
    {
        // All environment variables should be prefixed with DURABLETASK_
        public const string SidecarHost = "DURABLETASK_SIDECAR_HOST";
        public const string SidecarPort = "DURABLETASK_SIDECAR_PORT";
    }
}
