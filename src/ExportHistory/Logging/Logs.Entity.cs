// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Log messages.
/// </summary>
static partial class Logs
{
    [LoggerMessage(EventId = 101, Level = LogLevel.Information, Message = "Export job '{jobId}' is created")]
    public static partial void CreatedExportJob(this ILogger logger, string jobId);

    [LoggerMessage(EventId = 113, Level = LogLevel.Information, Message = "Export job '{jobId}' operation '{operationName}' info: {infoMessage}")]
    public static partial void ExportJobOperationInfo(this ILogger logger, string jobId, string operationName, string infoMessage);

    [LoggerMessage(EventId = 114, Level = LogLevel.Warning, Message = "Export job '{jobId}' operation '{operationName}' warning: {warningMessage}")]
    public static partial void ExportJobOperationWarning(this ILogger logger, string jobId, string operationName, string warningMessage);

    [LoggerMessage(EventId = 115, Level = LogLevel.Error, Message = "Operation '{operationName}' failed for export job '{jobId}': {errorMessage}")]
    public static partial void ExportJobOperationError(this ILogger logger, string jobId, string operationName, string errorMessage, Exception? exception = null);
}
