// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask
{
    /// <summary>
    /// Log messages.
    /// </summary>
    /// <remarks>
    /// NOTE: Trying to make logs consistent with https://github.com/Azure/durabletask/blob/main/src/DurableTask.Core/Logging/LogEvents.cs.
    /// </remarks>
    static partial class Logs
    {
        [LoggerMessage(EventId = 15, Level = LogLevel.Error, Message = "Unhandled exception in entity operation {entityInstanceId}/{operationName}.")]
        public static partial void OperationError(this ILogger logger, Exception ex, EntityInstanceId entityInstanceId, string operationName);

        [LoggerMessage(EventId = 55, Level = LogLevel.Information, Message = "{instanceId}: Evaluating custom retry handler for failed '{name}' task. Attempt = {attempt}.")]
        public static partial void RetryingTask(this ILogger logger, string instanceId, string name, int attempt);
    }
}
