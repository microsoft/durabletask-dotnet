// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Tracing;

/// <summary>
/// Schema for tracing events related to Durable Task operations.
/// </summary>
/// <remarks>
/// Adapted from "https://github.com/Azure/durabletask/blob/main/src/DurableTask.Core/Tracing/Schema.cs".
/// </remarks>
static class Schema
{
    /// <summary>
    /// Tags for tracing events related to orchestrations.
    /// </summary>
    public static class Task
    {
        /// <summary>
        /// The type of activity being executed, such as "orchestration", "activity", or "event".
        /// </summary>
        public const string Type = "durabletask.type";

        /// <summary>
        /// The name of the orchestration, activity, or event associated with the tracing event.
        /// </summary>
        public const string Name = "durabletask.task.name";

        /// <summary>
        /// The version of the orchestration or activity being executed.
        /// </summary>
        public const string Version = "durabletask.task.version";

        /// <summary>
        /// The ID of the orchestration instance associated with the tracing event.
        /// </summary>
        public const string InstanceId = "durabletask.task.instance_id";

        /// <summary>
        /// The execution ID of the orchestration instance associated with the tracing event.
        /// </summary>
        public const string ExecutionId = "durabletask.task.execution_id";

        /// <summary>
        /// The runtime status of the completed orchestration associated with the trace event.
        /// </summary>
        public const string Status = "durabletask.task.status";

        /// <summary>
        /// The event ID of the task being executed.
        /// </summary>
        public const string TaskId = "durabletask.task.task_id";

        /// <summary>
        /// The ID of the orchestration instance for which the event will be raised.
        /// </summary>
        public const string EventTargetInstanceId = "durabletask.event.target_instance_id";

        /// <summary>
        /// The time at which the timer is scheduled to fire.
        /// </summary>
        public const string FireAt = "durabletask.fire_at";
    }
}
