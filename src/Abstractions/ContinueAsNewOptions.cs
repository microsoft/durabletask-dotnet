// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Options for <see cref="TaskOrchestrationContext.ContinueAsNew(ContinueAsNewOptions, object?, bool)"/>.
/// </summary>
public class ContinueAsNewOptions
{
    /// <summary>
    /// Gets or sets the new version for the restarted orchestration instance.
    /// </summary>
    /// <remarks>
    /// When set, the framework uses this version to route the restarted instance to the
    /// appropriate orchestrator implementation. This is the safest migration point for
    /// eternal orchestrations since the history is fully reset, eliminating any replay
    /// conflict risk.
    /// </remarks>
    public string? NewVersion { get; set; }
}
