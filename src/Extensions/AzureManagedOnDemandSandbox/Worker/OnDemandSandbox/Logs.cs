// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using Proto = Microsoft.DurableTask.Protobuf.OnDemandSandbox;

namespace Microsoft.DurableTask.Worker.AzureManaged.OnDemandSandbox;

/// <summary>
/// Log messages for on-demand sandbox activity services.
/// </summary>
static partial class Logs
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "No on-demand sandbox activities discovered for hub={Hub}; skipping declaration")]
    public static partial void NoOnDemandSandboxActivitiesForDeclaration(ILogger logger, string hub);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "On-demand sandbox activities declared hub={Hub} workerProfile={WorkerProfile} count={Count} image={Image}")]
    public static partial void OnDemandSandboxActivitiesDeclared(ILogger logger, string hub, string workerProfile, int count, string image);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Error,
        Message = "On-demand sandbox activity declaration failed hub={Hub}")]
    public static partial void OnDemandSandboxActivityDeclarationFailed(ILogger logger, Exception exception, string hub);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Information,
        Message = "No on-demand sandbox activities discovered for worker hub={Hub}; skipping live registration")]
    public static partial void NoOnDemandSandboxActivitiesForWorkerRegistration(ILogger logger, string hub);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Information,
        Message = "On-demand sandbox activity worker registered hub={Hub} count={Count} substrate={Substrate} sandboxId={SandboxId}")]
    public static partial void OnDemandSandboxActivityWorkerRegistered(
        ILogger logger, string hub, int count, Proto.SubstrateKind substrate, string sandboxId);

    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Error,
        Message = "On-demand sandbox activity worker registration stream failed hub={Hub}")]
    public static partial void OnDemandSandboxActivityWorkerRegistrationFailed(ILogger logger, Exception exception, string hub);

    [LoggerMessage(
        EventId = 8,
        Level = LogLevel.Debug,
        Message = "Ignoring on-demand sandbox worker session completion failure during shutdown.")]
    public static partial void OnDemandSandboxWorkerSessionCompletionFailureIgnored(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 9,
        Level = LogLevel.Debug,
        Message = "Ignoring on-demand sandbox worker registration pump cancellation during shutdown.")]
    public static partial void OnDemandSandboxWorkerRegistrationPumpCancellationIgnored(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Debug,
        Message = "Ignoring on-demand sandbox worker registration pump failure during shutdown.")]
    public static partial void OnDemandSandboxWorkerRegistrationPumpFailureIgnored(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 11,
        Level = LogLevel.Debug,
        Message = "Ignoring on-demand sandbox worker session dispose failure during shutdown.")]
    public static partial void OnDemandSandboxWorkerSessionDisposeFailureIgnored(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 12,
        Level = LogLevel.Debug,
        Message = "Ignoring on-demand sandbox heartbeat pump cancellation after registration session completion.")]
    public static partial void OnDemandSandboxHeartbeatPumpCancellationIgnored(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 13,
        Level = LogLevel.Debug,
        Message = "Ignoring on-demand sandbox heartbeat pump failure after registration session completion.")]
    public static partial void OnDemandSandboxHeartbeatPumpFailureIgnored(ILogger logger, Exception exception);
}
