// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Dapr.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Dapr.DurableTask
{
    /// <summary>
    /// Log messages.
    /// </summary>
    static partial class Logs
    {
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

        /// <summary>
        /// Creates a logger named "Dapr.DurableTask.Worker" with an optional subcategory.
        /// </summary>
        /// <param name="loggerFactory">The logger factory to use to create the logger.</param>
        /// <param name="subcategory">The subcategory of the logger. For example, "Activities" or "Orchestrations".
        /// </param>
        /// <returns>The generated <see cref="ILogger"/>.</returns>
        internal static ILogger CreateWorkerLogger(ILoggerFactory loggerFactory, string? subcategory = null)
        {
            string categoryName = "Dapr.DurableTask.Worker";
            if (!string.IsNullOrEmpty(subcategory))
            {
                categoryName += "." + subcategory;
            }

            return loggerFactory.CreateLogger(categoryName);
        }
    }
}
