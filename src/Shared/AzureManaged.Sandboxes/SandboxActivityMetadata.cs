// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.AzureManaged.Internal;

/// <summary>
/// Shared normalization helpers for on-demand sandbox activity metadata.
/// </summary>
static class SandboxActivityMetadata
{
    /// <summary>
    /// Resolves configured activity names for on-demand sandbox activity execution.
    /// </summary>
    /// <param name="configuredNames">The configured activity names.</param>
    /// <returns>The normalized activity names.</returns>
    public static string[] ResolveActivityNames(IEnumerable<string> configuredNames)
    {
        return configuredNames
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Normalizes a worker profile ID.
    /// </summary>
    /// <param name="value">The worker profile ID.</param>
    /// <param name="errorMessage">The exception message to use when the value is empty.</param>
    /// <returns>The normalized worker profile ID.</returns>
    public static string NormalizeWorkerProfileId(string value, string errorMessage)
    {
        return NormalizeRequired(value, errorMessage);
    }

    /// <summary>
    /// Normalizes a required string.
    /// </summary>
    /// <param name="value">The value to normalize.</param>
    /// <param name="errorMessage">The exception message to use when the value is empty.</param>
    /// <returns>The normalized value.</returns>
    public static string NormalizeRequired(string value, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(errorMessage);
        }

        return value.Trim();
    }
}
