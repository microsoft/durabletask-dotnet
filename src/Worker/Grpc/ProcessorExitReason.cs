// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.Grpc;

/// <summary>
/// Indicates why <see cref="GrpcDurableTaskWorker.Processor.ExecuteAsync"/> returned to its caller.
/// </summary>
enum ProcessorExitReason
{
    /// <summary>
    /// The processor exited because cancellation was requested (graceful shutdown).
    /// </summary>
    Shutdown,

    /// <summary>
    /// The processor exited because the underlying gRPC channel appears poisoned and should be recreated
    /// before the next reconnect attempt. The caller is expected to obtain a fresh channel and rebuild the
    /// processor.
    /// </summary>
    ChannelRecreateRequested,
}
