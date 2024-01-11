// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Orchestration status options.
/// </summary>
///
public sealed class OrchestrationOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestrationOptions"/> class.
    /// </summary>
    public OrchestrationOptions()
    {
    }

    /// <summary>
    /// Enum describing the return value when attempting to reuse a previously used instance ID.
    /// </summary>
    public enum InstanceIdReuseAction
    {
        /// <summary>
        /// An error will be returned when attempting to reuse a previously used instance ID.
        /// </summary>
        ERROR,

        /// <summary>
        /// The request to reuse the previously used instanceID will be ignored.
        /// </summary>
        IGNORE,

        /// <summary>
        /// The currently running orchestration will be terminated and a new instance will be started.
        /// </summary>
        TERMINATE,
    }

    /// <summary>
    /// Enum describing the runtime status of the orchestration.
    /// </summary>
    public enum OrchestrationRuntimeStatus
    {
        /// <summary>
        /// The orchestration started running.
        /// </summary>
        Running,

        /// <summary>
        /// The orchestration completed normally.
        /// </summary>
        Completed,

        /// <summary>
        /// The orchestration is transitioning into a new instance.
        /// </summary>
        [Obsolete("The ContinuedAsNew status is obsolete and exists only for compatibility reasons.")]
        ContinuedAsNew,

        /// <summary>
        /// The orchestration completed with an unhandled exception.
        /// </summary>
        Failed,

        /// <summary>
        /// The orchestration canceled gracefully.
        /// </summary>
        [Obsolete("The Canceled status is not currently used and exists only for compatibility reasons.")]
        Canceled,

        /// <summary>
        /// The orchestration was abruptly terminated via a management API call.
        /// </summary>
        Terminated,

        /// <summary>
        /// The orchestration was scheduled but hasn't started running.
        /// </summary>
        Pending,

        /// <summary>
        /// The orchestration has been suspended.
        /// </summary>
        Suspended,
    }
}
