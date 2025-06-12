// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// NOTE: Modified from https://github.com/Azure/durabletask/blob/main/src/DurableTask.Core/Tracing/Schema.cs

namespace Microsoft.DurableTask.Tracing;

static class Schema
{
    internal static class Task
    {
        internal const string Type = "durabletask.type";
        internal const string Name = "durabletask.task.name";
        internal const string Version = "durabletask.task.version";
        internal const string InstanceId = "durabletask.task.instance_id";
        internal const string ExecutionId = "durabletask.task.execution_id";
        internal const string TaskId = "durabletask.task.task_id";
        internal const string EventTargetInstanceId = "durabletask.event.target_instance_id";
        internal const string FireAt = "durabletask.fire_at";
    }
}
