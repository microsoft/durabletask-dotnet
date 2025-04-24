// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;

namespace Microsoft.DurableTask;

/// <summary>
/// Utility class for generating the user agent string for the Durable Task SDK.
/// </summary>
public static class DurableTaskUserAgentUtil
{
    /// <summary>
    /// The name of the SDK used in the user agent string.
    /// </summary>
    static string SdkName => "durabletask-dotnet";

    /// <summary>
    /// Generates the user agent string for the Durable Task SDK based on a fixed name and the package version.
    /// </summary>
    /// <returns></returns>
    public static string GetUserAgent()
    {
        var version = typeof(DurableTaskUserAgentUtil).Assembly.GetName().Version;
        return $"{SdkName}/{version?.ToString() ?? "unknown"}";
    }
}