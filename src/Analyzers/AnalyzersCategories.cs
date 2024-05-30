// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Analyzers;

/// <summary>
/// Provides a set of well-known categories that are used by the analyzers diagnostics.
/// </summary>
static class AnalyzersCategories
{
    /// <summary>
    /// The category for the orchestration related analyzers.
    /// </summary>
    public const string Orchestration = "Orchestration";

    /// <summary>
    /// The category for the attribute binding related analyzers.
    /// </summary>
    public const string AttributeBinding = "Attribute Binding";

    /// <summary>
    /// The category for the activity related analyzers.
    /// </summary>
    public const string Activity = "Activity";
}
