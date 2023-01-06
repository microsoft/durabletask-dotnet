// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Accessor for the current <see cref="TaskOrchestrationContext" />.
/// </summary>
static class TaskOrchestrationContextAccessor
{
    static readonly AsyncLocal<TaskOrchestrationContext?> CurrentLocal = new();

    /// <summary>
    /// Gets or sets the current <see cref="TaskOrchestrationContext" />.
    /// </summary>
    public static TaskOrchestrationContext? Current
    {
        get => CurrentLocal.Value;
        set => CurrentLocal.Value = value;
    }
}
