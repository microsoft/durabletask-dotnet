// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.ScheduledTasks;
/// <summary>
/// Log messages.
/// </summary>
/// <remarks>
/// NOTE: Trying to make logs consistent with https://github.com/Azure/durabletask/blob/main/src/DurableTask.Core/Logging/LogEvents.cs.
/// </remarks>
static partial class Logs
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Creating schedule with options: {scheduleConfigurationCreateOptions}")]
    public static partial void CreatingSchedule(this ILogger logger, ScheduleCreationOptions scheduleConfigurationCreateOptions);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Updating schedule '{scheduleId}' with options: {scheduleConfigurationUpdateOptions}")]
    public static partial void UpdatingSchedule(this ILogger logger, string scheduleId, ScheduleUpdateOptions scheduleConfigurationUpdateOptions);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Pausing schedule '{scheduleId}'")]
    public static partial void PausingSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Resuming schedule '{scheduleId}'")]
    public static partial void ResumingSchedule(this ILogger logger, string scheduleId);

    // run schedule logging
    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Running schedule '{scheduleId}'")]
    public static partial void RunningSchedule(this ILogger logger, string scheduleId);
    
    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Deleting schedule '{scheduleId}'")]
    public static partial void DeletingSchedule(this ILogger logger, string scheduleId);
}