// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Dapr.DurableTask.Worker.Grpc
{
    /// <summary>
    /// Log messages.
    /// </summary>
    static partial class Logs
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Durable Task gRPC worker starting and connecting to {endpoint}.")]
        public static partial void StartingTaskHubWorker(this ILogger logger, string endpoint);

        [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Durable Task gRPC worker has disconnected from gRPC server.")]
        public static partial void SidecarDisconnected(this ILogger logger);

        [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "The gRPC server for Durable Task gRPC worker is unavailable. Will continue retrying.")]
        public static partial void SidecarUnavailable(this ILogger logger);

        [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Sidecar work-item streaming connection established.")]
        public static partial void EstablishedWorkItemConnection(this ILogger logger);

        [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Task hub NotFound. Will continue retrying.")]
        public static partial void TaskHubNotFound(this ILogger logger);

        [LoggerMessage(EventId = 10, Level = LogLevel.Debug, Message = "{instanceId}: Received request to run orchestrator '{name}' with {oldEventCount} replay and {newEventCount} new history events.")]
        public static partial void ReceivedOrchestratorRequest(this ILogger logger, string name, string instanceId, int oldEventCount, int newEventCount);

        [LoggerMessage(EventId = 11, Level = LogLevel.Debug, Message = "{instanceId}: Sending {count} action(s) [{actionsList}] for '{name}' orchestrator.")]
        public static partial void SendingOrchestratorResponse(this ILogger logger, string name, string instanceId, int count, string actionsList);

        [LoggerMessage(EventId = 12, Level = LogLevel.Warning, Message = "{instanceId}: '{name}' orchestrator failed with an unhandled exception: {details}.")]
        public static partial void OrchestratorFailed(this ILogger logger, string name, string instanceId, string details);

        [LoggerMessage(EventId = 13, Level = LogLevel.Debug, Message = "{instanceId}: Received request to run activity '{name}#{taskId}' with {sizeInBytes} bytes of input data.")]
        public static partial void ReceivedActivityRequest(this ILogger logger, string name, int taskId, string instanceId, int sizeInBytes);

        [LoggerMessage(EventId = 14, Level = LogLevel.Debug, Message = "{instanceId}: Sending {successOrFailure} response for '{name}#{taskId}' activity with {sizeInBytes} bytes of output data.")]
        public static partial void SendingActivityResponse(this ILogger logger, string successOrFailure, string name, int taskId, string instanceId, int sizeInBytes);

        [LoggerMessage(EventId = 20, Level = LogLevel.Error, Message = "Unexpected error in handling of instance ID '{instanceId}'.")]
        public static partial void UnexpectedError(this ILogger logger, Exception ex, string instanceId);

        [LoggerMessage(EventId = 21, Level = LogLevel.Warning, Message = "Received and dropped an unknown '{type}' work-item from the sidecar.")]
        public static partial void UnexpectedWorkItemType(this ILogger logger, string type);

        [LoggerMessage(EventId = 55, Level = LogLevel.Information, Message = "{instanceId}: Evaluating custom retry handler for failed '{name}' task. Attempt = {attempt}.")]
        public static partial void RetryingTask(this ILogger logger, string instanceId, string name, int attempt);

        [LoggerMessage(EventId = 56, Level = LogLevel.Warning, Message = "Channel to backend has stopped receiving traffic, will attempt to reconnect.")]
        public static partial void ConnectionTimeout(this ILogger logger);

        [LoggerMessage(EventId = 57, Level = LogLevel.Warning, Message = "Orchestration version did not meet worker versioning requirements. Error action = '{errorAction}'. Version error = '{versionError}'")]
        public static partial void OrchestrationVersionFailure(this ILogger logger, string errorAction, string versionError);

        [LoggerMessage(EventId = 58, Level = LogLevel.Information, Message = "Abandoning orchestration. InstanceId = '{instanceId}'. Completion token = '{completionToken}'")]
        public static partial void AbandoningOrchestrationDueToVersioning(this ILogger logger, string instanceId, string completionToken);
    }
}
