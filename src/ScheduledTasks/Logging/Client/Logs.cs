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
// TODO: Do we really need all of these?
static partial class Logs
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Client: Creating schedule with options: {scheduleConfigCreateOptions}")]
    public static partial void ClientCreatingSchedule(this ILogger logger, ScheduleCreationOptions scheduleConfigCreateOptions);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Client: Getting schedule handle for schedule '{scheduleId}'")]
    public static partial void ClientGettingScheduleHandle(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Client: Listing initialized schedules")]
    public static partial void ClientListingSchedules(this ILogger logger);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Client: Describing schedule '{scheduleId}'")]
    public static partial void ClientDescribingSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Client: Pausing schedule '{scheduleId}'")]
    public static partial void ClientPausingSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Client: Resuming schedule '{scheduleId}'")]
    public static partial void ClientResumingSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "Client: Updating schedule '{scheduleId}'")]
    public static partial void ClientUpdatingSchedule(this ILogger logger, string scheduleId);

    [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message = "Client: Deleting schedule '{scheduleId}'")]
    public static partial void ClientDeletingSchedule(this ILogger logger, string scheduleId);
}
