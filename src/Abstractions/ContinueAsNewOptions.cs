// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Options for <see cref="TaskOrchestrationContext.ContinueAsNew(ContinueAsNewOptions)"/>.
/// </summary>
public class ContinueAsNewOptions
{
    /// <summary>
    /// Gets or sets the JSON-serializable input data to re-initialize the instance with.
    /// </summary>
    public object? NewInput { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to preserve unprocessed external events
    /// across the restart. Defaults to <c>true</c>.
    /// </summary>
    /// <remarks>
    /// When set to <c>true</c>, any unprocessed external events are re-added into the new execution
    /// history when the orchestration instance restarts. When <c>false</c>, any unprocessed
    /// external events will be discarded when the orchestration instance restarts.
    /// </remarks>
    public bool PreserveUnprocessedEvents { get; set; } = true;

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
