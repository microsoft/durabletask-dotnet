// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Internal helpers for preserving activity version-selection semantics across worker dispatch.
/// </summary>
static class ActivityVersioning
{
    /// <summary>
    /// Internal tag stamped on scheduled activity events to communicate whether the requested activity version
    /// is an explicit caller-supplied selection or an inherited orchestration-instance version. The worker reads
    /// this tag to choose between strict dispatch (no fallback) and inherited dispatch (fallback to the
    /// unversioned registration is allowed). When the tag is missing on a versioned request, the worker fails
    /// closed (treats it as <see cref="ExplicitSource"/>) so a sidecar that drops tags cannot silently degrade
    /// strict-explicit semantics to inherited-fallback.
    /// </summary>
    internal const string VersionSourceTagName = "microsoft.durabletask.activity.version-source";

    /// <summary>
    /// Tag value indicating the caller explicitly chose this activity version via <see cref="ActivityOptions.Version"/>.
    /// Strict dispatch — no unversioned fallback.
    /// </summary>
    internal const string ExplicitSource = "explicit";

    /// <summary>
    /// Tag value indicating the activity inherited its version from the orchestration instance. Inherited
    /// dispatch — fallback to the unversioned registration is allowed for backward compatibility.
    /// </summary>
    internal const string InheritedSource = "inherited";

    /// <summary>
    /// All reserved version-source tag keys. Stripped from caller-supplied <see cref="TaskOptions.Tags"/> to
    /// prevent spoofing of the dispatch contract.
    /// </summary>
    internal static readonly string[] ReservedTagKeys = { VersionSourceTagName };
}

