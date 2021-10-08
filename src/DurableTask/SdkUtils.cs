//  ----------------------------------------------------------------------------------
//  Copyright Microsoft Corporation
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//  http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//  ----------------------------------------------------------------------------------

using System;
using DurableTask.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DurableTask;

static class SdkUtils
{
    internal static readonly IServiceProvider EmptyServiceProvider = new ServiceCollection().BuildServiceProvider();
    internal static readonly IDataConverter DefaultDataConverter = JsonDataConverter.Default;

    /// <summary>
    /// Helper for validating addresses passed to the <see cref="TaskHubGrpcWorker"/> and <see cref="TaskHubGrpcClient"/> types.
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

    internal static ILogger GetLogger(ILoggerFactory loggerFactory) => loggerFactory.CreateLogger("DurableTask.Sdk");

    /// <summary>
    /// Gets a serialized representation of an error payload. The format of this error is considered the standard that all SDKs should adhere to.
    /// </summary>
    internal static string GetSerializedErrorPayload(IDataConverter dataConverter, string message, Exception? e = null)
    {
        return GetSerializedErrorPayload(dataConverter, message, e?.ToString());
    }

    /// <summary>
    /// Gets a serialized representation of an error payload. The format of this error is considered the standard that all SDKs should adhere to.
    /// </summary>
    internal static string GetSerializedErrorPayload(IDataConverter dataConverter, string message, string? fullText)
    {
        return dataConverter.Serialize(new OrchestrationFailureDetails(message, fullText));
    }

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
