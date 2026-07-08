// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Options used to create the singleton blob payload auto-purge job.
/// </summary>
/// <param name="PurgeBatchSize">
/// The maximum number of tombstoned payloads to request from the backend per cycle.
/// </param>
public sealed record BlobPurgeJobCreationOptions(int PurgeBatchSize);
