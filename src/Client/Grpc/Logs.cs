// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Client.Grpc
{
    /// <summary>
    /// Log messages.
    /// </summary>
    /// <remarks>
    /// NOTE: Trying to make logs consistent with https://github.com/Azure/durabletask/blob/main/src/DurableTask.Core/Logging/LogEvents.cs.
    /// </remarks>
    static partial class Logs
    {
        [LoggerMessage(EventId = 40, Level = LogLevel.Information, Message = "Scheduling new {name} orchestration with instance ID '{instanceId}' and {sizeInBytes} bytes of input data.")]
#pragma warning disable SYSLIB1015 // Argument is not referenced from the logging message
        public static partial void SchedulingOrchestration(this ILogger logger, string instanceId, string name, int sizeInBytes, DateTimeOffset startTime);
#pragma warning restore SYSLIB1015 // Argument is not referenced from the logging message

        // NOTE: fetchInputsAndOutputs seems like something that could be left out of the text message and just included in the structured logs
        [LoggerMessage(EventId = 42, Level = LogLevel.Information, Message = "Waiting for instance '{instanceId}' to start.")]
#pragma warning disable SYSLIB1015 // Argument is not referenced from the logging message
        public static partial void WaitingForInstanceStart(this ILogger logger, string instanceId, bool fetchInputsAndOutputs);
#pragma warning restore SYSLIB1015 // Argument is not referenced from the logging message

        // NOTE: fetchInputsAndOutputs seems like something that could be left out of the text message and just included in the structured logs
        [LoggerMessage(EventId = 43, Level = LogLevel.Information, Message = "Waiting for instance '{instanceId}' to complete, fail, or terminate.")]
#pragma warning disable SYSLIB1015 // Argument is not referenced from the logging message
        public static partial void WaitingForInstanceCompletion(this ILogger logger, string instanceId, bool fetchInputsAndOutputs);
#pragma warning restore SYSLIB1015 // Argument is not referenced from the logging message

        [LoggerMessage(EventId = 44, Level = LogLevel.Information, Message = "Terminating instance '{instanceId}'.")]
        public static partial void TerminatingInstance(this ILogger logger, string instanceId);

        [LoggerMessage(EventId = 45, Level = LogLevel.Information, Message = "Purging instance metadata '{instanceId}'.")]
        public static partial void PurgingInstanceMetadata(this ILogger logger, string instanceId);

        [LoggerMessage(EventId = 46, Level = LogLevel.Information, Message = "Purging instances with filter: {{ CreatedFrom = {createdFrom}, CreatedTo = {createdTo}, Statuses = {statuses} }}")]
        public static partial void PurgingInstances(this ILogger logger, DateTimeOffset? createdFrom, DateTimeOffset? createdTo, string? statuses);

        [LoggerMessage(EventId = 80, Level = LogLevel.Warning, Message = "Recreating gRPC channel to backend after {failureCount} consecutive transport failures.")]
        public static partial void RecreatingChannel(this ILogger logger, int failureCount);

        [LoggerMessage(EventId = 81, Level = LogLevel.Information, Message = "gRPC channel to backend has been recreated. New target: {endpoint}.")]
        public static partial void ChannelRecreated(this ILogger logger, string endpoint);

        [LoggerMessage(EventId = 82, Level = LogLevel.Warning, Message = "gRPC channel recreation failed.")]
        public static partial void ChannelRecreateFailed(this ILogger logger, Exception exception);

        /// <summary>
        /// <see cref="PurgingInstances(ILogger, DateTimeOffset?, DateTimeOffset?, string?)" />.
        /// </summary>
        /// <param name="logger">The logger to log to.</param>
        /// <param name="filter">The filter to log.</param>
        public static void PurgingInstances(this ILogger logger, PurgeInstancesFilter filter)
        {
            string? statuses = filter?.Statuses is null ? null : string.Join("|", filter.Statuses);
            PurgingInstances(logger, filter?.CreatedFrom, filter?.CreatedTo, statuses);
        }
    }
}
