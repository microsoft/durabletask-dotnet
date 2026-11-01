// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client.Grpc.Internal;

/// <summary>
/// Exposes the backend's large-payload auto-purge setting to the Azure Blob payloads extension, which owns the
/// public API that turns the feature on and off.
/// </summary>
/// <remarks>
/// <para>
/// This is an internal API that supports the DurableTask infrastructure and not subject to
/// the same compatibility standards as public APIs. It may be changed or removed without notice in
/// any release. You should not implement it, and should not use it directly in your code. Doing so
/// can result in application failures when updating to a new DurableTask release.
/// </para>
/// <para>
/// Deliberately narrow. The extension needs exactly one backend operation, and it must reach it over the
/// client's own transport - the same post-interceptor <c>CallInvoker</c> the client uses for every other RPC,
/// so the call carries the configured auth chain and follows channel recreation. Handing out the raw
/// <c>CallInvoker</c> instead would let a caller build arbitrary clients on the transport. The other two purge
/// RPCs are not here: they are executed by the worker's activities on the worker's transport, and no client
/// ever calls them.
/// </para>
/// <para>
/// Implemented explicitly by <see cref="GrpcDurableTaskClient"/>, so a client that is not the gRPC client - or
/// a gRPC client from an SDK version that predates this - fails the cast rather than silently doing nothing.
/// </para>
/// </remarks>
public interface ILargePayloadAutoPurgeClient
{
    /// <summary>
    /// Sets the large-payload blob auto-purge setting for the caller's authenticated task hub.
    /// </summary>
    /// <param name="enabled">The setting to persist.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A task that completes once the backend has acknowledged the setting.</returns>
    Task SetLargePayloadAutoPurgeAsync(bool enabled, CancellationToken cancellation = default);
}
