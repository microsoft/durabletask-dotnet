// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask
{
    /// <summary>
    /// Log messages.
    /// </summary>
    static partial class Logs
    {
        /// <summary>
        /// The common category name for worker logs.
        /// </summary>
        internal const string WorkerCategoryName = "Microsoft.DurableTask.Worker";

        [LoggerMessage(EventId = 15, Level = LogLevel.Error, Message = "Unhandled exception in entity operation {entityInstanceId}/{operationName}.")]
        public static partial void OperationError(this ILogger logger, Exception ex, EntityInstanceId entityInstanceId, string operationName);

        [LoggerMessage(EventId = 55, Level = LogLevel.Information, Message = "{instanceId}: Evaluating custom retry handler for failed '{name}' task. Attempt = {attempt}.")]
        public static partial void RetryingTask(this ILogger logger, string instanceId, string name, int attempt);

        [LoggerMessage(EventId = 600, Level = LogLevel.Information, Message = "'{Name}' orchestration with ID '{InstanceId}' started.")]
        public static partial void OrchestrationStarted(this ILogger logger, string instanceId, string name);

        [LoggerMessage(EventId = 601, Level = LogLevel.Information, Message = "'{Name}' orchestration with ID '{InstanceId}' completed.")]
        public static partial void OrchestrationCompleted(this ILogger logger, string instanceId, string name);

        [LoggerMessage(EventId = 602, Level = LogLevel.Information, Message = "'{Name}' orchestration with ID '{InstanceId}' failed.")]
        public static partial void OrchestrationFailed(this ILogger logger, Exception ex, string instanceId, string name);

        [LoggerMessage(EventId = 603, Level = LogLevel.Information, Message = "'{Name}' activity of orchestration ID '{InstanceId}' started.")]
        public static partial void ActivityStarted(this ILogger logger, string instanceId, string name);

        [LoggerMessage(EventId = 604, Level = LogLevel.Information, Message = "'{Name}' activity of orchestration ID '{InstanceId}' completed.")]
        public static partial void ActivityCompleted(this ILogger logger, string instanceId, string name);

        [LoggerMessage(EventId = 605, Level = LogLevel.Information, Message = "'{Name}' activity of orchestration ID '{InstanceId}' failed.")]
        public static partial void ActivityFailed(this ILogger logger, Exception ex, string instanceId, string name);
    }
}
