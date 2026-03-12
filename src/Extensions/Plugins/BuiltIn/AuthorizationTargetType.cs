// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Plugins.BuiltIn;

/// <summary>
/// The type of authorization target.
/// </summary>
public enum AuthorizationTargetType
{
    /// <summary>
    /// The target is an orchestration.
    /// </summary>
    Orchestration,

    /// <summary>
    /// The target is an activity.
    /// </summary>
    Activity,
}
