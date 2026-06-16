// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using Proto = Microsoft.DurableTask.Protobuf.Sandboxes;

namespace Microsoft.DurableTask.Worker.AzureManaged.Sandboxes;

/// <summary>
/// Log messages for on-demand sandbox activity services.
/// </summary>
static partial class Logs
{
    [LoggerMessage(
        EventId = 701,
        Level = LogLevel.Information,
        Message = "On-demand sandbox activity worker registered hub={Hub} count={Count} sandboxProvider={SandboxProvider} sandboxId={SandboxId}")]
    public static partial void SandboxActivityWorkerRegistered(
        ILogger logger, string hub, int count, Proto.SandboxProviderKind sandboxProvider, string sandboxId);

    [LoggerMessage(
        EventId = 702,
        Level = LogLevel.Error,
        Message = "On-demand sandbox activity worker registration stream failed hub={Hub}")]
    public static partial void SandboxActivityWorkerRegistrationFailed(ILogger logger, Exception exception, string hub);

    [LoggerMessage(
        EventId = 703,
        Level = LogLevel.Debug,
        Message = "Ignoring on-demand sandbox worker session completion failure during shutdown.")]
    public static partial void SandboxWorkerSessionCompletionFailureIgnored(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 704,
        Level = LogLevel.Debug,
        Message = "Ignoring on-demand sandbox worker registration loop cancellation during shutdown.")]
    public static partial void SandboxWorkerRegistrationLoopCancellationIgnored(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 705,
        Level = LogLevel.Debug,
        Message = "Ignoring on-demand sandbox worker registration loop failure during shutdown.")]
    public static partial void SandboxWorkerRegistrationLoopFailureIgnored(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 706,
        Level = LogLevel.Debug,
        Message = "Ignoring on-demand sandbox worker session dispose failure during shutdown.")]
    public static partial void SandboxWorkerSessionDisposeFailureIgnored(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 707,
        Level = LogLevel.Debug,
        Message = "Ignoring on-demand sandbox heartbeat loop cancellation after registration session completion.")]
    public static partial void SandboxHeartbeatLoopCancellationIgnored(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 708,
        Level = LogLevel.Debug,
        Message = "Ignoring on-demand sandbox heartbeat loop failure after registration session completion.")]
    public static partial void SandboxHeartbeatLoopFailureIgnored(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 709,
        Level = LogLevel.Debug,
        Message = "Ignoring on-demand sandbox worker session completion failure after heartbeat loop failure.")]
    public static partial void SandboxWorkerSessionCompletionAfterHeartbeatFailureIgnored(ILogger logger, Exception exception);
}
