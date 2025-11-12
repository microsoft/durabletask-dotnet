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
    }
}
