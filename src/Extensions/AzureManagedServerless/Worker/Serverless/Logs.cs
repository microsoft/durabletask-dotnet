// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using Proto = Microsoft.DurableTask.Protobuf.Serverless;

namespace Microsoft.DurableTask.Worker.AzureManaged.Serverless;

/// <summary>
/// Log messages for serverless activity services.
/// </summary>
static partial class Logs
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "No serverless activities discovered for hub={Hub}; skipping declaration")]
    public static partial void NoServerlessActivitiesForDeclaration(ILogger logger, string hub);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Serverless activities declared hub={Hub} workerProfile={WorkerProfile} count={Count} image={Image}")]
    public static partial void ServerlessActivitiesDeclared(ILogger logger, string hub, string workerProfile, int count, string image);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Serverless activity declaration failed transiently hub={Hub} attempt={Attempt} maxAttempts={MaxAttempts}")]
    public static partial void ServerlessActivityDeclarationRetry(ILogger logger, Exception exception, string hub, int attempt, int maxAttempts);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Error,
        Message = "Serverless activity declaration failed hub={Hub}")]
    public static partial void ServerlessActivityDeclarationFailed(ILogger logger, Exception exception, string hub);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Information,
        Message = "No serverless activities discovered for worker hub={Hub}; skipping live registration")]
    public static partial void NoServerlessActivitiesForWorkerRegistration(ILogger logger, string hub);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Information,
        Message = "Serverless activity worker registered hub={Hub} worker={Worker} count={Count} substrate={Substrate} sandboxId={SandboxId}")]
    public static partial void ServerlessActivityWorkerRegistered(
        ILogger logger, string hub, string worker, int count, Proto.SubstrateKind substrate, string sandboxId);

    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Error,
        Message = "Serverless activity worker registration stream failed hub={Hub}")]
    public static partial void ServerlessActivityWorkerRegistrationFailed(ILogger logger, Exception exception, string hub);
}
