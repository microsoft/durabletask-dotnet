// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Internal helpers for preserving activity version-selection semantics across worker dispatch.
/// </summary>
static class ActivityVersioning
{
    /// <summary>
    /// Internal tag stamped on scheduled activity events when the caller explicitly chooses an activity version.
    /// </summary>
    internal const string ExplicitVersionTagName = "microsoft.durabletask.activity.explicit-version";
}
