//  ----------------------------------------------------------------------------------
//  Copyright Microsoft Corporation
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//  http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//  ----------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;

namespace DurableTask
{
    static partial class Logs
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Task hub server is connecting to {address}.")]
        public static partial void StartingTaskHubServer(this ILogger logger, string address);

        [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Task hub server has disconnected from {address}.")]
        public static partial void TaskHubServerDisconnected(this ILogger logger, string address);

        [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "Received orchestrator request for '{name}' (instance ID '{instanceId}').")]
        public static partial void ReceivedOrchestratorRequest(this ILogger logger, string name, string instanceId);

        [LoggerMessage(EventId = 4, Level = LogLevel.Debug, Message = "Sending orchestrator response for '{name}' (instance ID = '{instanceId}') with {count} new action(s).")]
        public static partial void SendingOrchestratorResponse(this ILogger logger, string name, string instanceId, int count);

        [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "The orchestrator '{name}' (instance ID '{instanceId}') failed with an unhandled exception: {details}.")]
        public static partial void OrchestratorFailed(this ILogger logger, string name, string instanceId, string details);

        [LoggerMessage(EventId = 6, Level = LogLevel.Debug, Message = "Received activity request for '{name}#{taskId}' (instance ID '{instanceId}') with {sizeInBytes} bytes of input data.")]
        public static partial void ReceivedActivityRequest(this ILogger logger, string name, int taskId, string instanceId, int sizeInBytes);

        [LoggerMessage(EventId = 7, Level = LogLevel.Debug, Message = "Sending activity response for '{name}#{taskId}' (instance ID = '{instanceId}') with {sizeInBytes} bytes of output data.")]
        public static partial void SendingActivityResponse(this ILogger logger, string name, int taskId, string instanceId, int sizeInBytes);

        [LoggerMessage(EventId = 20, Level = LogLevel.Error, Message = "Unexpected error in handling of instance ID '{instanceId}'. Details: {details}")]
        public static partial void UnexpectedError(this ILogger logger, string instanceId, string details);

        [LoggerMessage(EventId = 21, Level = LogLevel.Warning, Message = "Received a request of type `{type}` from the worker, but didn't know how to handle it.")]
        public static partial void UnexpectedRequestType(this ILogger logger, string type);

        [LoggerMessage(EventId = 22, Level = LogLevel.Warning, Message = "Connection to the worker failed. Attempting to reconnect.")]
        public static partial void ConnectionFailed(this ILogger logger);

        [LoggerMessage(EventId = 23, Level = LogLevel.Warning, Message = "The worker is busy servicing other clients. Waiting for the worker to become available for a new connection.")]
        public static partial void WorkerBusy(this ILogger logger);
    }
}
