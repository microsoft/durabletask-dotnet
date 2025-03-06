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
        [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "Entity support not enabled via options. Entities will be disabled.")]
        public static partial void EntitiesDisabled(this ILogger logger);

        [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Entity support is enabled, but the IDurableTaskFactory does not support entities.")]
        public static partial void TaskFactoryDoesNotSupportEntities(this ILogger logger);
    }
}
