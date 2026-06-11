// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using Proto = Microsoft.DurableTask.Protobuf.OnDemandSandbox;

namespace Microsoft.DurableTask.Worker.AzureManaged.Sandboxes;

/// <summary>
/// Log messages for on-demand sandbox activity services.
/// </summary>
static partial class Logs
{
    [LoggerMessage(
        EventId = 701,
        Level = LogLevel.Information,
        Message = "On-demand sandbox activity worker registered hub={Hub} count={Count} substrate={Substrate} sandboxId={SandboxId}")]
    public static partial void OnDemandSandboxActivityWorkerRegistered(
        ILogger logger, string hub, int count, Proto.SubstrateKind substrate, string sandboxId);

    [LoggerMessage(
        EventId = 702,
        Level = LogLevel.Error,
        Message = "On-demand sandbox activity worker registration stream failed hub={Hub}")]
    public static partial void OnDemandSandboxActivityWorkerRegistrationFailed(ILogger logger, Exception exception, string hub);

    [LoggerMessage(
        EventId = 703,
        Level = LogLevel.Debug,
        Message = "Ignoring on-demand sandbox worker session completion failure during shutdown.")]
    public static partial void OnDemandSandboxWorkerSessionCompletionFailureIgnored(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 704,
        Level = LogLevel.Debug,
        Message = "Ignoring on-demand sandbox worker registration pump cancellation during shutdown.")]
    public static partial void OnDemandSandboxWorkerRegistrationPumpCancellationIgnored(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 705,
        Level = LogLevel.Debug,
        Message = "Ignoring on-demand sandbox worker registration pump failure during shutdown.")]
    public static partial void OnDemandSandboxWorkerRegistrationPumpFailureIgnored(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 706,
        Level = LogLevel.Debug,
        Message = "Ignoring on-demand sandbox worker session dispose failure during shutdown.")]
    public static partial void OnDemandSandboxWorkerSessionDisposeFailureIgnored(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 707,
        Level = LogLevel.Debug,
        Message = "Ignoring on-demand sandbox heartbeat pump cancellation after registration session completion.")]
    public static partial void OnDemandSandboxHeartbeatPumpCancellationIgnored(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 708,
        Level = LogLevel.Debug,
        Message = "Ignoring on-demand sandbox heartbeat pump failure after registration session completion.")]
    public static partial void OnDemandSandboxHeartbeatPumpFailureIgnored(ILogger logger, Exception exception);
}
