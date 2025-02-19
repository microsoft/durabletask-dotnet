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
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Schedule is being created with options: {scheduleConfigurationCreateOptions}")]
    public static partial void CreatingSchedule(this ILogger logger, ScheduleCreationOptions scheduleConfigurationCreateOptions);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Schedule '{scheduleId}' is created")]
    public static partial void CreatedSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Schedule '{scheduleId}' is being updated with options: {scheduleConfigurationUpdateOptions}")]
    public static partial void UpdatingSchedule(this ILogger logger, string scheduleId, ScheduleUpdateOptions scheduleConfigurationUpdateOptions);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Schedule '{scheduleId}' is updated")]
    public static partial void UpdatedSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Schedule '{scheduleId}' is being paused")]
    public static partial void PausingSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Schedule '{scheduleId}' is paused")]
    public static partial void PausedSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "Schedule '{scheduleId}' is being resumed")]
    public static partial void ResumingSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message = "Schedule '{scheduleId}' is resumed")]
    public static partial void ResumedSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 9, Level = LogLevel.Information, Message = "Schedule '{scheduleId}' is running")]
    public static partial void RunningSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "Schedule '{scheduleId}' is executed")]
    public static partial void CompletedScheduleRun(this ILogger logger, string scheduleId);
    
    [LoggerMessage(EventId = 11, Level = LogLevel.Information, Message = "Schedule '{scheduleId}' is being deleted")]
    public static partial void DeletingSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 12, Level = LogLevel.Information, Message = "Schedule '{scheduleId}' is deleted")]
    public static partial void DeletedSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 13, Level = LogLevel.Information, Message = "Schedule '{scheduleId}' operation '{operationName}' info: {infoMessage}")]
    public static partial void ScheduleOperationInfo(this ILogger logger, string scheduleId, string operationName, string infoMessage);

    [LoggerMessage(EventId = 14, Level = LogLevel.Warning, Message = "Schedule '{scheduleId}' operation '{operationName}' warning: {warningMessage}")]
    public static partial void ScheduleOperationWarning(this ILogger logger, string scheduleId, string operationName, string warningMessage);

    [LoggerMessage(EventId = 15, Level = LogLevel.Error, Message = "Operation '{operationName}' failed for schedule '{scheduleId}': {errorMessage}")]
    public static partial void ScheduleOperationError(this ILogger logger, string scheduleId, string operationName, string errorMessage, Exception? exception = null);

    [LoggerMessage(EventId = 16, Level = LogLevel.Information, Message = "Schedule '{scheduleId}' run cancelled with execution token '{executionToken}'")]
    public static partial void ScheduleRunCancelled(this ILogger logger, string scheduleId, string executionToken);
}