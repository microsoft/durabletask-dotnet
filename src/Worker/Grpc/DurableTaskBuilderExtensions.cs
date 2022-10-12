﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Extension methods for registering gRPC to <see cref="IDurableTaskBuilder" />.
/// </summary>
public static class DurableTaskBuilderExtensions
{
    /// <summary>
    /// Configures the <see cref="IDurableTaskBuilder" /> to use the gRPC worker.
    /// </summary>
    /// <param name="builder">The builder to configure.</param>
    /// <returns>The original builder, for call chaining.</returns>
    /// <remarks>
    /// <b>Note:</b> only 1 instance of gRPC worker is supported per sidecar.
    /// </remarks>
    public static IDurableTaskBuilder UseGrpc(this IDurableTaskBuilder builder)
        => builder.UseGrpc(opt => { });

    /// <summary>
    /// Configures the <see cref="IDurableTaskBuilder" /> to use the gRPC worker.
    /// </summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="channel">The gRPC channel to use.</param>
    /// <returns>The original builder, for call chaining.</returns>
    /// <remarks>
    /// <b>Note:</b> only 1 instance of gRPC worker is supported per sidecar.
    /// </remarks>
    public static IDurableTaskBuilder UseGrpc(this IDurableTaskBuilder builder, Channel channel)
        => builder.UseGrpc(opt => opt.Channel = channel);

    /// <summary>
    /// Configures the <see cref="IDurableTaskBuilder" /> to use the gRPC worker.
    /// </summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="address">The gRPC address to use.</param>
    /// <returns>The original builder, for call chaining.</returns>
    /// <remarks>
    /// <b>Note:</b> only 1 instance of gRPC worker is supported per sidecar.
    /// </remarks>
    public static IDurableTaskBuilder UseGrpc(this IDurableTaskBuilder builder, string address)
        => builder.UseGrpc(opt => opt.Address = address);

    /// <summary>
    /// Configures the <see cref="IDurableTaskBuilder" /> to use the gRPC worker.
    /// </summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="configure">The callback for configuring gRPC options.</param>
    /// <returns>The original builder, for call chaining.</returns>
    /// <remarks>
    /// <b>Note:</b> only 1 instance of gRPC worker is supported per sidecar.
    /// </remarks>
    public static IDurableTaskBuilder UseGrpc(
        this IDurableTaskBuilder builder, Action<GrpcDurableTaskWorkerOptions> configure)
    {
        if (builder.Name != Options.DefaultName)
        {
            throw new InvalidOperationException("gRPC does not support named builders.");
        }

        builder.Services.Configure(builder.Name, configure);
        return builder.UseBuildTarget<GrpcDurableTaskWorker>();
    }
}
