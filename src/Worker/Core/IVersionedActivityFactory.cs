// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Creates activity instances by logical name and requested version.
/// Implementations may use an unversioned registration as a compatibility fallback when no exact version match exists.
/// </summary>
internal interface IVersionedActivityFactory
{
    /// <summary>
    /// Tries to create an activity that matches the provided logical name and version.
    /// </summary>
    /// <param name="name">The activity name.</param>
    /// <param name="version">The activity version.</param>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="activity">The created activity, if found.</param>
    /// <returns><c>true</c> if a matching activity was created; otherwise <c>false</c>.</returns>
    bool TryCreateActivity(
        TaskName name,
        TaskVersion version,
        IServiceProvider serviceProvider,
        [NotNullWhen(true)] out ITaskActivity? activity);
}
