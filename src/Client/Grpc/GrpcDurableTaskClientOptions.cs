// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;

namespace Microsoft.DurableTask.Client.Grpc;

/// <summary>
/// The gRPC client options.
/// </summary>
public sealed class GrpcDurableTaskClientOptions
{
    /// <summary>
    /// Gets or sets the address of the gRPC endpoint to connect to. Default is 127.0.0.1:4001.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Gets or sets the gRPC channel to use. Will supersede <see cref="Address" /> when provided.
    /// </summary>
    public Channel? Channel { get; set; }
}
