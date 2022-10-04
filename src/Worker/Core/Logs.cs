// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask
{
    // NOTE: Trying to make logs consistent with https://github.com/Azure/durabletask/blob/main/src/DurableTask.Core/Logging/LogEvents.cs.
    static partial class Logs
    {
        [LoggerMessage(EventId = 55, Level = LogLevel.Information, Message = "{instanceId}: Evaluating custom retry handler for failed '{name}' task. Attempt = {attempt}.")]
        public static partial void RetryingTask(this ILogger logger, string instanceId, string name, int attempt);
    }
}
