// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.AzureManaged.Internal;

/// <summary>
/// Shared normalization helpers for on-demand sandbox activity metadata.
/// </summary>
static class SandboxActivityMetadata
{
    /// <summary>
    /// Resolves configured activities for on-demand sandbox activity execution.
    /// </summary>
    /// <param name="configuredActivities">The configured activities.</param>
    /// <returns>The normalized activities.</returns>
    public static Activity[] ResolveActivities(IEnumerable<Activity> configuredActivities)
    {
        List<Activity> activities = [];
        foreach (Activity activity in configuredActivities)
        {
            if (string.IsNullOrWhiteSpace(activity.Name))
            {
                continue;
            }

            Activity normalized = new(
                activity.Name.Trim(),
                NormalizeOptional(activity.Version));
            if (!activities.Any(existing => ActivityEquals(existing, normalized)))
            {
                activities.Add(normalized);
            }
        }

        return activities.ToArray();
    }

    /// <summary>
    /// Normalizes an optional string.
    /// </summary>
    /// <param name="value">The value to normalize.</param>
    /// <returns>The trimmed value, or <see langword="null" /> if it is empty.</returns>
    public static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Determines whether two activities represent the same activity identity.
    /// </summary>
    /// <param name="left">The left activity.</param>
    /// <param name="right">The right activity.</param>
    /// <returns><see langword="true" /> if the activities are equal; otherwise, <see langword="false" />.</returns>
    public static bool ActivityEquals(Activity left, Activity right) =>
        string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.Version, right.Version, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Determines whether two activity filters can match overlapping work.
    /// </summary>
    /// <param name="left">The left activity.</param>
    /// <param name="right">The right activity.</param>
    /// <returns><see langword="true" /> if the activities overlap; otherwise, <see langword="false" />.</returns>
    public static bool ActivitiesOverlap(Activity left, Activity right) =>
        string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase)
        && (left.Version is null
            || right.Version is null
            || string.Equals(left.Version, right.Version, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Formats an activity identity for messages.
    /// </summary>
    /// <param name="activity">The activity identity.</param>
    /// <returns>The formatted activity identity.</returns>
    public static string FormatActivity(Activity activity) =>
        activity.Version is null ? activity.Name : $"{activity.Name}@{activity.Version}";

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

    /// <summary>
    /// Represents a sandbox activity identity.
    /// </summary>
    /// <param name="Name">The activity name.</param>
    /// <param name="Version">The activity version, or <see langword="null" /> for wildcard/unversioned execution.</param>
    public readonly record struct Activity(string Name, string? Version);
}
