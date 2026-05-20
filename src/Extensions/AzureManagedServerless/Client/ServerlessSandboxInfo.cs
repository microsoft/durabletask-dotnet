// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client.AzureManaged;

/// <summary>
/// A DTS-managed sandbox that can execute serverless activities for a worker profile.
/// </summary>
/// <param name="DtsSandboxIdentifier">The DTS-generated sandbox identifier injected into the worker as DTS_SANDBOX_ID.</param>
/// <param name="WorkerProfileId">The worker profile associated with the sandbox.</param>
/// <param name="CreatedAt">The time when the sandbox was created.</param>
/// <param name="State">The current sandbox state reported by DTS.</param>
public sealed record ServerlessSandboxInfo(
    string DtsSandboxIdentifier,
    string WorkerProfileId,
    DateTimeOffset CreatedAt,
    string State);
