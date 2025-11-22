// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Log messages.
/// </summary>
static partial class Logs
{
    [LoggerMessage(EventId = 80, Level = LogLevel.Information, Message = "Creating export job with options: {exportJobCreationOptions}")]
    public static partial void ClientCreatingExportJob(this ILogger logger, ExportJobCreationOptions exportJobCreationOptions);

    [LoggerMessage(EventId = 84, Level = LogLevel.Information, Message = "Deleting export job '{jobId}'")]
    public static partial void ClientDeletingExportJob(this ILogger logger, string jobId);

    [LoggerMessage(EventId = 87, Level = LogLevel.Error, Message = "{message} (JobId: {jobId})")]
    public static partial void ClientError(this ILogger logger, string message, string jobId, Exception? exception = null);
}
