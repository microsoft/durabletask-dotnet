// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Dapr.DurableTask.Worker.Grpc;
using Microsoft.Extensions.DependencyInjection;

namespace Dapr.DurableTask.Worker;

/// <summary>
/// Extension methods for registering gRPC to <see cref="IDurableTaskWorkerBuilder" />.
/// </summary>
public static class DurableTaskWorkerBuilderExtensions
{
    /// <summary>
    /// Configures the <see cref="IDurableTaskWorkerBuilder" /> to use the gRPC worker.
    /// </summary>
    /// <param name="builder">The builder to configure.</param>
    /// <returns>The original builder, for call chaining.</returns>
    /// <remarks>
    /// <b>Note:</b> only 1 instance of gRPC worker is supported per sidecar.
    /// </remarks>
    public static IDurableTaskWorkerBuilder UseGrpc(this IDurableTaskWorkerBuilder builder)
        => builder.UseGrpc(opt => { });

    /// <summary>
    /// Configures the <see cref="IDurableTaskWorkerBuilder" /> to be a gRPC client.
    /// </summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="channel">The channel for the Durable Task sidecar endpoint.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder UseGrpc(this IDurableTaskWorkerBuilder builder, GrpcChannel channel)
        => builder.UseGrpc(opt => opt.Channel = channel);

    /// <summary>
    /// Configures the <see cref="IDurableTaskWorkerBuilder" /> to use the gRPC worker.
    /// </summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="address">The gRPC address to use.</param>
    /// <returns>The original builder, for call chaining.</returns>
    /// <remarks>
    /// <b>Note:</b> only 1 instance of gRPC worker is supported per sidecar.
    /// </remarks>
    public static IDurableTaskWorkerBuilder UseGrpc(this IDurableTaskWorkerBuilder builder, string address)
        => builder.UseGrpc(opt => opt.Address = address);

    /// <summary>
    /// Configures the <see cref="IDurableTaskWorkerBuilder" /> to use the gRPC worker.
    /// </summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="configure">The callback for configuring gRPC options.</param>
    /// <returns>The original builder, for call chaining.</returns>
    /// <remarks>
    /// <b>Note:</b> only 1 instance of gRPC worker is supported per sidecar.
    /// </remarks>
    public static IDurableTaskWorkerBuilder UseGrpc(
        this IDurableTaskWorkerBuilder builder, Action<GrpcDurableTaskWorkerOptions> configure)
    {
        Check.NotNull(builder);
        Check.NotNull(configure);
        builder.Services.Configure(builder.Name, configure);
        return builder.UseBuildTarget<GrpcDurableTaskWorker, GrpcDurableTaskWorkerOptions>();
    }
}
