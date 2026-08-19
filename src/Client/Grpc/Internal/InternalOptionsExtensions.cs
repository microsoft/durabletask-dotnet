// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client.Grpc.Internal;

/// <summary>
/// Provides access to configuring internal options for the gRPC client.
/// </summary>
public static class InternalOptionsExtensions
{
    /// <summary>
    /// Sets a callback that the client invokes when the underlying gRPC channel needs to be recreated
    /// after repeated transport failures (e.g., because the backend was replaced and the existing channel
    /// is wedged on a half-open HTTP/2 connection). The callback receives the channel the client last
    /// observed and must return either a freshly created channel or the currently cached channel if a
    /// peer client has already swapped it. Implementations are responsible for atomic swap and deferred
    /// disposal of the old channel so in-flight RPCs from peer clients are not interrupted.
    /// </summary>
    /// <param name="options">The gRPC client options.</param>
    /// <param name="recreator">The recreate callback.</param>
    /// <remarks>
    /// This is an internal API that supports the DurableTask infrastructure and not subject to
    /// the same compatibility standards as public APIs. It may be changed or removed without notice in
    /// any release. You should only use it directly in your code with extreme caution and knowing that
    /// doing so can result in application failures when updating to a new DurableTask release.
    /// </remarks>
    public static void SetChannelRecreator(
        this GrpcDurableTaskClientOptions options,
        Func<GrpcChannel, CancellationToken, Task<GrpcChannel>> recreator)
    {
        options.Internal.ChannelRecreator = recreator ?? throw new ArgumentNullException(nameof(recreator));
    }
}
