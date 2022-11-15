// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;

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
    /// Gets or sets the gRPC channel to use. Will supersede <see cref="Address" /> when provided.
    /// </summary>
    public Channel? Channel { get; set; }
}
