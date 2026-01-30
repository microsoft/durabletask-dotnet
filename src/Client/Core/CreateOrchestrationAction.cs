// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Defines actions for handling orchestration instance ID conflicts.
/// </summary>
public enum CreateOrchestrationAction
{
    /// <summary>
    /// Throws an exception if an orchestration instance with the specified ID already exists in one of the operation statuses.
    /// This is the default behavior.
    /// </summary>
    Error = 0,

    /// <summary>
    /// Ignores the request to create a new orchestration instance if one already exists in one of the operation statuses.
    /// No exception is thrown and no new instance is created.
    /// </summary>
    Ignore = 1,

    /// <summary>
    /// Terminates any existing orchestration instance with the same ID that is in one of the operation statuses,
    /// and then creates a new instance as an atomic operation. This is similar to an on-demand ContinueAsNew.
    /// </summary>
    Terminate = 2,
}
