﻿// Copyright (c) Microsoft Corporation.
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
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Creating schedule with options: {scheduleConfigCreateOptions}")]
    public static partial void ClientCreatingSchedule(this ILogger logger, ScheduleCreationOptions scheduleConfigCreateOptions);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Getting schedule handle for schedule '{scheduleId}'")]
    public static partial void ClientGettingScheduleHandle(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Pausing schedule '{scheduleId}'")]
    public static partial void ClientPausingSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Resuming schedule '{scheduleId}'")]
    public static partial void ClientResumingSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "Updating schedule '{scheduleId}'")]
    public static partial void ClientUpdatingSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message = "Deleting schedule '{scheduleId}'")]
    public static partial void ClientDeletingSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 9, Level = LogLevel.Information, Message = "{message} (ScheduleId: {scheduleId})")]
    public static partial void ClientInfo(this ILogger logger, string message, string scheduleId);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning, Message = "{message} (ScheduleId: {scheduleId})")]
    public static partial void ClientWarning(this ILogger logger, string message, string scheduleId);

    [LoggerMessage(EventId = 11, Level = LogLevel.Error, Message = "{message} (ScheduleId: {scheduleId})")]
    public static partial void ClientError(this ILogger logger, string message, string scheduleId);
}
