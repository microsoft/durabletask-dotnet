// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// An unsupported-backend observation made by one activation's runner.
/// </summary>
/// <param name="Generation">The runner's activation generation.</param>
/// <param name="Detail">The observed failure detail.</param>
sealed record BlobPurgeUnsupportedRequest(string Generation, string Detail);
