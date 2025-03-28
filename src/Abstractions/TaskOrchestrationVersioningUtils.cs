// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Abstractions;

/// <summary>
/// Utilities for handling Orchestration/Task versioning operations.
/// </summary>
public static class TaskOrchestrationVersioningUtils
{
    /// <summary>
    /// Compare two versions to each other.
    /// </summary>
    /// <remarks>
    /// This method's comparison is handled in the following order:
    ///   1. The versions are checked if they are empty (non-versioned). Both being empty signifies equality.
    ///   2. If sourceVersion is empty but otherVersion is defined, this is treated as the source being less than the other.
    ///   3. If otherVersion is empty but sourceVersion is defined, this is treated as the source being greater than the other.
    ///   4. Both versions are attempted to be parsed into System.Version and compared as such.
    ///   5. If all else fails, a direct string comparison is done between the versions.
    /// </remarks>
    /// <param name="sourceVersion">The source version that will be compared against the other version.</param>
    /// <param name="otherVersion">The other version to compare against.</param>
    /// <returns>An int representing how sourceVersion compares to otherVersion.</returns>
    public static int CompareVersions(string sourceVersion, string otherVersion)
    {
        // Both versions are empty, treat as equal.
        if (string.IsNullOrWhiteSpace(sourceVersion) && string.IsNullOrWhiteSpace(otherVersion))
        {
            return 0;
        }

        // An empty version in the context is always less than a defined version in the parameter.
        if (string.IsNullOrWhiteSpace(sourceVersion))
        {
            return -1;
        }

        // An empty version in the parameter is always less than a defined version in the context.
        if (string.IsNullOrWhiteSpace(otherVersion))
        {
            return 1;
        }

        // If both versions use the .NET Version class, return that comparison.
        if (System.Version.TryParse(sourceVersion, out Version parsedSourceVersion) && System.Version.TryParse(otherVersion, out Version parsedOtherVersion))
        {
            return parsedSourceVersion.CompareTo(parsedOtherVersion);
        }

        // If we have gotten to here, we don't know the syntax of the versions we are comparing, use a string comparison as a final check.
        return string.Compare(sourceVersion, otherVersion, StringComparison.OrdinalIgnoreCase);
    }
}
