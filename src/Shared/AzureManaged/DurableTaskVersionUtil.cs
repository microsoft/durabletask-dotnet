// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;

namespace Microsoft.DurableTask;

/// <summary>
/// Utility class for generating the user agent string for the Durable Task SDK.
/// </summary>
public static class DurableTaskUserAgentUtil
{
    /// <summary>
    /// The name of the SDK used in the user agent string.
    /// </summary>
    const string SdkName = "durabletask-dotnet";

    /// <summary>
    /// The version of the SDK used in the user agent string.
    /// </summary>
    static readonly string PackageVersion = FileVersionInfo.GetVersionInfo(typeof(DurableTaskUserAgentUtil).Assembly.Location).FileVersion;

    /// <summary>
    /// Generates the user agent string for the Durable Task SDK based on a fixed name, the package version, and the component type.
    /// </summary>
    /// <param name="componentType">The type of component (Client or Worker).</param>
    /// <returns>The user agent string.</returns>
    public static string GetUserAgent(string componentType)
    {
        return $"{SdkName}/{PackageVersion?.ToString() ?? "unknown"} ({componentType})";
    }
}