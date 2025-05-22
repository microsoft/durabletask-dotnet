// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.Command;
using Microsoft.Extensions.Logging;

namespace Dapr.DurableTask.Sidecar
{
    static partial class Logs
    {
        [LoggerMessage(
            EventId = 5,
            Level = LogLevel.Information,
            Message = "Waiting for a remote client to connect to this server. Total wait time: {totalWaitTime:c}")]
        public static partial void WaitingForClientConnection(
            this ILogger logger,
            TimeSpan totalWaitTime);

        [LoggerMessage(
            EventId = 6,
            Level = LogLevel.Information,
            Message = "Received work-item connection from {address}. Client connection deadline = {deadline:s}.")]
        public static partial void ClientConnected(
            this ILogger logger,
            string address,
            DateTime deadline);

        [LoggerMessage(
            EventId = 7,
            Level = LogLevel.Information,
            Message = "Client at {address} has disconnected. No further work-items will be processed until a new connection is established.")]
        public static partial void ClientDisconnected(
            this ILogger logger,
            string address);

        [LoggerMessage(
            EventId = 22,
            Level = LogLevel.Information,
            Message = "{dispatcher}: Shutting down, waiting for {currentWorkItemCount} active work-items to complete.")]
        public static partial void DispatcherStopping(
            this ILogger logger,
            string dispatcher,
            int currentWorkItemCount);

        [LoggerMessage(
            EventId = 23,
            Level = LogLevel.Trace,
            Message = "{dispatcher}: Fetching next work item. Current active work-items: {currentWorkItemCount}/{maxWorkItemCount}.")]
        public static partial void FetchWorkItemStarting(
            this ILogger logger,
            string dispatcher,
            int currentWorkItemCount,
            int maxWorkItemCount);

        [LoggerMessage(
            EventId = 24,
            Level = LogLevel.Trace,
            Message = "{dispatcher}: Fetched next work item '{workItemId}' after {latencyMs}ms. Current active work-items: {currentWorkItemCount}/{maxWorkItemCount}.")]
        public static partial void FetchWorkItemCompleted(
            this ILogger logger,
            string dispatcher,
            string workItemId,
            long latencyMs,
            int currentWorkItemCount,
            int maxWorkItemCount);

        [LoggerMessage(
            EventId = 25,
            Level = LogLevel.Error,
            Message = "{dispatcher}: Unexpected {action} failure for work-item '{workItemId}': {details}")]
        public static partial void DispatchWorkItemFailure(
            this ILogger logger,
            string dispatcher,
            string action,
            string workItemId,
            string details);

        [LoggerMessage(
            EventId = 26,
            Level = LogLevel.Information,
            Message = "{dispatcher}: Work-item fetching is paused: {details}. Current active work-item count: {currentWorkItemCount}/{maxWorkItemCount}.")]
        public static partial void FetchingThrottled(
            this ILogger logger,
            string dispatcher,
            string details,
            int currentWorkItemCount,
            int maxWorkItemCount);

        [LoggerMessage(
            EventId = 49,
            Level = LogLevel.Information,
            Message = "{instanceId}: Orchestrator '{name}' completed with a {runtimeStatus} status and {sizeInBytes} bytes of output.")]
        public static partial void OrchestratorCompleted(
            this ILogger logger,
            string instanceId,
            string name,
            OrchestrationStatus runtimeStatus,
            int sizeInBytes);

        [LoggerMessage(
            EventId = 51,
            Level = LogLevel.Debug,
            Message = "{instanceId}: Preparing to execute orchestrator '{name}' with {eventCount} new events: {newEvents}")]
        public static partial void OrchestratorExecuting(
            this ILogger logger,
            string instanceId,
            string name,
            int eventCount,
            string newEvents);

        [LoggerMessage(
            EventId = 55,
            Level = LogLevel.Warning,
            Message = "{instanceId}: Ignoring unknown orchestrator action '{action}'.")]
        public static partial void IgnoringUnknownOrchestratorAction(
            this ILogger logger,
            string instanceId,
            OrchestratorActionType action);
    }
}