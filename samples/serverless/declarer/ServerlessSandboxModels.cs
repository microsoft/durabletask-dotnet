// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Samples.Serverless.Declarer;

public sealed record ServerlessSandboxHttpOptions(
    string TaskHub,
    string DefaultWorkerProfileId);

public sealed record ServerlessSandboxListResponse(
    string TaskHub,
    string WorkerProfileId,
    IReadOnlyList<ServerlessSandboxSummary> Sandboxes);

public sealed record ServerlessSandboxSummary(
    string DtsSandboxIdentifier,
    string WorkerProfileId,
    string State,
    DateTimeOffset? CreatedAt);