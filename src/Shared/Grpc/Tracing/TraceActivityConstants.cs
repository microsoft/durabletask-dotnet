// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// Modified from https://github.com/Azure/durabletask/blob/main/src/DurableTask.Core/Tracing/TraceActivityConstants.cs.
namespace Microsoft.DurableTask.Tracing;

/// <summary>
/// Constants for trace activity names used in Durable Task Framework.
/// </summary>
static class TraceActivityConstants
{
    /// <summary>
    /// The name of the activity that represents orchestration operations.
    /// </summary>
    public const string Orchestration = "orchestration";

    /// <summary>
    /// The name of the activity that represents activity operations.
    /// </summary>
    public const string Activity = "activity";

    /// <summary>
    /// The name of the activity that represents entity operations.
    /// </summary>
    public const string Event = "event";

    /// <summary>
    /// The name of the activity that represents timer operations.
    /// </summary>
    public const string Timer = "timer";

    /// <summary>
    /// The name of the activity that represents an operation to create an orchestration.
    /// </summary>
    public const string CreateOrchestration = "create_orchestration";

    /// <summary>
    /// The name of the activity that represents an operation to raise an event.
    /// </summary>
    public const string OrchestrationEvent = "orchestration_event";
}
