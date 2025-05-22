// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Dapr.DurableTask.Client.Grpc;

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
}
