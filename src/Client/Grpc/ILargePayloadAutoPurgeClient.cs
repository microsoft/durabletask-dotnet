// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client.Grpc;

/// <summary>
/// Exposes the backend's large-payload auto-purge setting to the Azure Blob payloads extension, which owns the
/// public API that turns the feature on and off.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately narrow, and deliberately not public. The extension needs exactly one backend operation, and it
/// must reach it over the client's own transport - the same post-interceptor <c>CallInvoker</c> the client uses
/// for every other RPC, so the call carries the configured auth chain and follows channel recreation. Handing
/// out the raw <c>CallInvoker</c> instead would let a caller build arbitrary clients on the transport and would
/// turn an implementation detail into supported surface. The other two purge RPCs are not here: they are
/// executed by the worker's activities on the worker's transport, and no client ever calls them.
/// </para>
/// <para>
/// Implemented explicitly by <see cref="GrpcDurableTaskClient"/>, so a client that is not the gRPC client - or a
/// gRPC client from an SDK version that predates this - fails the cast rather than silently doing nothing.
/// </para>
/// </remarks>
interface ILargePayloadAutoPurgeClient
{
    /// <summary>
    /// Sets the large-payload blob auto-purge setting for the caller's authenticated task hub.
    /// </summary>
    /// <param name="enabled">The setting to persist.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A task that completes once the backend has acknowledged the setting.</returns>
    Task SetLargePayloadAutoPurgeAsync(bool enabled, CancellationToken cancellation = default);
}
