// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;

namespace Microsoft.DurableTask;

/// <summary>
/// Static factory for creating large payload interceptors without exposing internal implementation details.
/// </summary>
public static class AzureBlobPayloadCallInvokerFactory
{
    /// <summary>
    /// Creates a CallInvoker with large payload support interceptor applied to the given GrpcChannel.
    /// </summary>
    /// <param name="channel">The gRPC channel to intercept.</param>
    /// <param name="options">The large payload storage options.</param>
    /// <returns>A CallInvoker with the large payload interceptor applied.</returns>
    public static CallInvoker Create(GrpcChannel channel, LargePayloadStorageOptions options)
    {
        IPayloadStore payloadStore = new BlobPayloadStore(options);
        return channel.CreateCallInvoker().Intercept(new AzureBlobPayloadsAzureManagedBackendInterceptor(payloadStore, options));
    }
}
