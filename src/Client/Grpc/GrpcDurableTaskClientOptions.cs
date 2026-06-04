// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client.Grpc;

/// <summary>
/// The gRPC client options.
/// </summary>
public sealed class GrpcDurableTaskClientOptions : DurableTaskClientOptions
{
    /// <summary>
    /// Gets or sets the address of the gRPC endpoint to connect to. Default is localhost:4001.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Gets or sets the gRPC channel to use. Will supersede <see cref="CallInvoker" /> when provided.
    /// </summary>
    public GrpcChannel? Channel { get; set; }

    /// <summary>
    /// Gets or sets the gRPC call invoker to use. Will supersede <see cref="Address" /> when provided.
    /// </summary>
    public CallInvoker? CallInvoker { get; set; }

    /// <summary>
    /// Gets the internal options. These are not exposed directly, but configurable via
    /// <see cref="Internal.InternalOptionsExtensions"/>.
    /// </summary>
    internal InternalOptions Internal { get; } = new();

    /// <summary>
    /// Internal options are not exposed directly, but configurable via <see cref="Internal.InternalOptionsExtensions"/>.
    /// </summary>
    internal class InternalOptions
    {
        /// <summary>
        /// Gets or sets the number of consecutive transport failures (Unavailable responses, or
        /// DeadlineExceeded responses on RPCs other than long-poll waits) after which the underlying
        /// gRPC channel will be recreated to clear stale DNS, sub-channel state, or routing-affinity
        /// bindings. Setting to 0 or a negative value disables channel recreation. Defaults to 5.
        /// </summary>
        public int ChannelRecreateFailureThreshold { get; set; } = 5;

        /// <summary>
        /// Gets or sets the minimum interval between consecutive channel recreate attempts. Acts as a
        /// cooldown so a burst of failures during a real outage cannot thrash the channel cache.
        /// Defaults to 30 seconds.
        /// </summary>
        public TimeSpan MinRecreateInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets an optional callback invoked when the client requests a fresh gRPC channel after
        /// repeated transport failures. The callback receives the previously-used channel and should
        /// return either a freshly created channel or the currently cached channel if a peer has already
        /// recreated it. Implementations are responsible for atomic swap and deferred disposal of the
        /// old channel so in-flight RPCs from peer clients are not interrupted.
        /// </summary>
        public Func<GrpcChannel, CancellationToken, Task<GrpcChannel>>? ChannelRecreator { get; set; }
    }
}
