// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Dapr.DurableTask.ScheduledTasks;

/// <summary>
/// Log messages.
/// </summary>
static partial class Logs
{
    [LoggerMessage(EventId = 100, Level = LogLevel.Information, Message = "Schedule '{scheduleId}' is being created")]
    public static partial void CreatingSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 101, Level = LogLevel.Information, Message = "Schedule '{scheduleId}' is created")]
    public static partial void CreatedSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 102, Level = LogLevel.Information, Message = "Schedule '{scheduleId}' is being updated")]
    public static partial void UpdatingSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 103, Level = LogLevel.Information, Message = "Schedule '{scheduleId}' is updated")]
    public static partial void UpdatedSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 104, Level = LogLevel.Information, Message = "Schedule '{scheduleId}' is being paused")]
    public static partial void PausingSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 105, Level = LogLevel.Information, Message = "Schedule '{scheduleId}' is paused")]
    public static partial void PausedSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 106, Level = LogLevel.Information, Message = "Schedule '{scheduleId}' is being resumed")]
    public static partial void ResumingSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 107, Level = LogLevel.Information, Message = "Schedule '{scheduleId}' is resumed")]
    public static partial void ResumedSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 108, Level = LogLevel.Information, Message = "Schedule '{scheduleId}' is running")]
    public static partial void RunningSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 109, Level = LogLevel.Information, Message = "Schedule '{scheduleId}' is executed")]
    public static partial void CompletedScheduleRun(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 110, Level = LogLevel.Information, Message = "Schedule '{scheduleId}' is being deleted")]
    public static partial void DeletingSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 111, Level = LogLevel.Information, Message = "Schedule '{scheduleId}' is deleted")]
    public static partial void DeletedSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 112, Level = LogLevel.Debug, Message = "Schedule '{scheduleId}' operation '{operationName}' debug: {debugMessage}")]
    public static partial void ScheduleOperationDebug(this ILogger logger, string scheduleId, string operationName, string debugMessage);

    [LoggerMessage(EventId = 113, Level = LogLevel.Information, Message = "Schedule '{scheduleId}' operation '{operationName}' info: {infoMessage}")]
    public static partial void ScheduleOperationInfo(this ILogger logger, string scheduleId, string operationName, string infoMessage);

    [LoggerMessage(EventId = 114, Level = LogLevel.Warning, Message = "Schedule '{scheduleId}' operation '{operationName}' warning: {warningMessage}")]
    public static partial void ScheduleOperationWarning(this ILogger logger, string scheduleId, string operationName, string warningMessage);

    [LoggerMessage(EventId = 115, Level = LogLevel.Error, Message = "Operation '{operationName}' failed for schedule '{scheduleId}': {errorMessage}")]
    public static partial void ScheduleOperationError(this ILogger logger, string scheduleId, string operationName, string errorMessage, Exception? exception = null);

    [LoggerMessage(EventId = 116, Level = LogLevel.Information, Message = "Schedule '{scheduleId}' run cancelled with execution token '{executionToken}'")]
    public static partial void ScheduleRunCancelled(this ILogger logger, string scheduleId, string executionToken);
}
