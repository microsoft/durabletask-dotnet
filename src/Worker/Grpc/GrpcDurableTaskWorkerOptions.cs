// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc;

/// <summary>
/// The gRPC worker options.
/// </summary>
public sealed class GrpcDurableTaskWorkerOptions : DurableTaskWorkerOptions
{
    /// <summary>
    /// The minimum allowed size (in bytes) for complete orchestration work item chunks.
    /// </summary>
    public const int MinCompleteOrchestrationWorkItemChunkSizeInBytes = 1 * 1024 * 1024; // 1 MB

    /// <summary>
    /// The maximum allowed size (in bytes) for complete orchestration work item chunks.
    /// </summary>
    public const int MaxCompleteOrchestrationWorkItemChunkSizeInBytes = 4_089_446; // 3.9 MB

    int completeOrchestrationWorkItemChunkSizeInBytes = MaxCompleteOrchestrationWorkItemChunkSizeInBytes;

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
    /// Gets the collection of capabilities enabled on this worker.
    /// Capabilities are announced to the backend on connection.
    /// </summary>
    public HashSet<P.WorkerCapability> Capabilities { get; } = new() { P.WorkerCapability.HistoryStreaming };

    /// <summary>
    /// Gets or sets the maximum size of all actions in a complete orchestration work item chunk.
    /// The default value is 3.9MB. We leave some headroom to account for request size overhead.
    /// </summary>
    /// <remarks>
    /// This value is used to limit the size of the complete orchestration work item request.
    /// If the response exceeds this limit, it will be automatically split into multiple chunks of maximum size CompleteOrchestrationWorkItemChunkSizeInBytes
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the value is less than 1 MB or greater than 3.9 MB.
    /// </exception>
    public int CompleteOrchestrationWorkItemChunkSizeInBytes
    {
        get => this.completeOrchestrationWorkItemChunkSizeInBytes;
        set
        {
            if (value < MinCompleteOrchestrationWorkItemChunkSizeInBytes ||
                value > MaxCompleteOrchestrationWorkItemChunkSizeInBytes)
            {
                string message = $"{nameof(CompleteOrchestrationWorkItemChunkSizeInBytes)} must be between " +
                    $"{MinCompleteOrchestrationWorkItemChunkSizeInBytes} bytes (1 MB) and " +
                    $"{MaxCompleteOrchestrationWorkItemChunkSizeInBytes} bytes (3.9 MB), inclusive.";
                throw new ArgumentOutOfRangeException(
                    nameof(this.CompleteOrchestrationWorkItemChunkSizeInBytes),
                    value,
                    message);
            }

            this.completeOrchestrationWorkItemChunkSizeInBytes = value;
        }
    }

    /// <summary>
    /// Gets the internal protocol options. These are used to control backend-dependent features.
    /// </summary>
    internal InternalOptions Internal { get; } = new();

    /// <summary>
    /// Internal options are not exposed directly, but configurable via <see cref="Internal.InternalOptionsExtensions"/>.
    /// </summary>
    internal class InternalOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether entity-related events appearing in orchestration histories should be
        /// automatically converted back and forth between the old DT Core representation (JSON-encoded external events)
        /// and the new protobuf representation (explicit history events), which is used by the DTS scheduler backend.
        /// </summary>
        public bool ConvertOrchestrationEntityEvents { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to automatically add entity
        /// unlock events into the history when an orchestration terminates while holding an entity lock.
        /// </summary>
        public bool InsertEntityUnlocksOnCompletion { get; set; }

        /// <summary>
        /// Gets or sets the maximum amount of time to wait for the initial Hello handshake against the
        /// backend before treating the connect attempt as failed and retrying. A non-positive value disables
        /// the deadline. Defaults to 30 seconds. This guards against half-open HTTP/2 connections that can
        /// otherwise cause reconnect to hang indefinitely.
        /// </summary>
        public TimeSpan HelloDeadline { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets the maximum amount of time the worker will wait between messages on an established
        /// work-item stream before treating the channel as silently disconnected and forcing a reconnect.
        /// The backend sends periodic health-ping work items expressly to keep this window alive when no
        /// real work is flowing, so this value should be larger than the server's ping cadence to avoid
        /// false positives. Defaults to 120 seconds. A non-positive value disables silent-disconnect detection.
        /// </summary>
        public TimeSpan SilentDisconnectTimeout { get; set; } = TimeSpan.FromSeconds(120);

        /// <summary>
        /// Gets or sets the number of consecutive connect failures (Hello timeouts, Unavailable responses, or
        /// silent stream disconnects) after which the underlying gRPC channel will be recreated to clear
        /// stale DNS, sub-channel state, or routing-affinity bindings. Setting to 0 or a negative value
        /// disables channel recreation. Defaults to 5.
        /// </summary>
        public int ChannelRecreateFailureThreshold { get; set; } = 5;

        /// <summary>
        /// Gets or sets the base delay used when computing reconnect backoff with full jitter:
        /// the actual delay is uniformly random in [0, min(cap, base * 2^attempt)]. Defaults to 1 second.
        /// </summary>
        public TimeSpan ReconnectBackoffBase { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Gets or sets the maximum delay used when computing reconnect backoff with full jitter.
        /// Defaults to 30 seconds.
        /// </summary>
        public TimeSpan ReconnectBackoffCap { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets an optional callback invoked when the worker requests a fresh gRPC channel after
        /// repeated connect failures. The callback receives the previously-used channel and should return
        /// its replacement. Implementations are responsible for publishing the replacement channel and
        /// deferring disposal of the old channel so in-flight RPCs already using it are not interrupted.
        /// </summary>
        public Func<GrpcChannel, CancellationToken, Task<GrpcChannel>>? ChannelRecreator { get; set; }
    }
}
