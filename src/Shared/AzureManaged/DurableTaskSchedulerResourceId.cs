// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Resolves and normalizes token audiences independently of endpoints and credential authorities.
/// </summary>
static class DurableTaskSchedulerResourceId
{
    /// <summary>
    /// Gets the default audience for a new options instance.
    /// </summary>
    /// <returns>The government or public cloud audience.</returns>
    public static string GetDefault()
    {
        string? region = Environment.GetEnvironmentVariable("REGION_NAME");
        return region is not null
            && (region.StartsWith("usgov", StringComparison.OrdinalIgnoreCase)
                || region.StartsWith("usdod", StringComparison.OrdinalIgnoreCase))
            ? "https://durabletask.azure.us"
            : "https://durabletask.io";
    }

    /// <summary>
    /// Normalizes an explicitly configured audience, or returns null for an omitted value.
    /// </summary>
    /// <param name="resourceId">The token audience URI, not an ARM resource path.</param>
    /// <returns>The normalized audience, or null to use the options instance's default.</returns>
    public static string? Normalize(string? resourceId)
    {
        if (string.IsNullOrEmpty(resourceId))
        {
            return null;
        }

        string normalized = resourceId.Trim().TrimEnd('/');
        const string ScopeSuffix = "/.default";
        if (normalized.EndsWith(ScopeSuffix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^ScopeSuffix.Length].TrimEnd('/');
        }

        if (normalized.Length == 0)
        {
            throw new ArgumentException(
                "ResourceId must contain a token audience URI after removing whitespace, trailing slashes, and a /.default suffix.",
                nameof(resourceId));
        }

        return normalized;
    }
}
