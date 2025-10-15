// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using P = Microsoft.DurableTask.AzureManagedBackend.Protobuf;

namespace Microsoft.DurableTask;

/// <summary>
/// gRPC interceptor that externalizes large payloads to an <see cref="PayloadStore"/> on requests
/// and resolves known payload tokens on responses for Azure Managed Backend.
/// </summary>
public sealed class AzureBlobPayloadsManagedBackendInterceptor(PayloadStore payloadStore, LargePayloadStorageOptions options)
    : BasePayloadInterceptor<object, object>(payloadStore, options)
{
    protected override async Task ExternalizeRequestPayloadsAsync<TRequest>(TRequest request, CancellationToken cancellation)
    {
        
    }

    protected override async Task ResolveResponsePayloadsAsync<TResponse>(TResponse response, CancellationToken cancellation)
    {
        throw new NotImplementedException();
    }
}
