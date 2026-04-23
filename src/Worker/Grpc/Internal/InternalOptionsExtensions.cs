// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.DurableTask.Worker.Grpc.Internal;

/// <summary>
/// Provides access to configuring internal options for the gRPC worker.
/// </summary>
public static class InternalOptionsExtensions
{
    /// <summary>
    /// Configure the worker to use the default settings for connecting to the Azure Managed Durable Task service.
    /// </summary>
    /// <param name="options">The gRPC worker options.</param>
    /// <remarks>
    /// This is an internal API that supports the DurableTask infrastructure and not subject to
    /// the same compatibility standards as public APIs. It may be changed or removed without notice in
    /// any release. You should only use it directly in your code with extreme caution and knowing that
    /// doing so can result in application failures when updating to a new DurableTask release.
    /// </remarks>
    public static void ConfigureForAzureManaged(this GrpcDurableTaskWorkerOptions options)
    {
        options.Internal.ConvertOrchestrationEntityEvents = true;
        options.Internal.InsertEntityUnlocksOnCompletion = true;
    }

        /// <summary>
        /// Sets a callback that the worker invokes when the underlying gRPC channel needs to be recreated
        /// after repeated connect failures (e.g., because the backend was replaced and the existing channel
        /// is wedged on a half-open HTTP/2 connection). The callback receives the channel the worker last
        /// observed and must return its replacement. Implementations are responsible for publishing the
        /// replacement channel and deferring disposal of the old channel so in-flight RPCs already using it
        /// are not interrupted.
        /// </summary>
        /// <param name="options">The gRPC worker options.</param>
        /// <param name="recreator">The recreate callback.</param>
    /// <remarks>
    /// This is an internal API that supports the DurableTask infrastructure and not subject to
    /// the same compatibility standards as public APIs. It may be changed or removed without notice in
    /// any release. You should only use it directly in your code with extreme caution and knowing that
    /// doing so can result in application failures when updating to a new DurableTask release.
    /// </remarks>
    public static void SetChannelRecreator(
        this GrpcDurableTaskWorkerOptions options,
        Func<GrpcChannel, CancellationToken, Task<GrpcChannel>> recreator)
    {
        options.Internal.ChannelRecreator = recreator ?? throw new ArgumentNullException(nameof(recreator));
    }

    /// <summary>
    /// Sets the deadline applied to the initial <c>Hello</c> RPC during worker connect. A wedged
    /// handshake on a half-open HTTP/2 connection no longer hangs the reconnect loop indefinitely.
    /// </summary>
    /// <param name="options">The gRPC worker options.</param>
    /// <param name="deadline">The deadline; non-positive disables the deadline.</param>
    /// <remarks>
    /// This is an internal API that supports the DurableTask infrastructure and not subject to
    /// the same compatibility standards as public APIs. It may be changed or removed without notice in
    /// any release.
    /// </remarks>
    public static void SetHelloDeadline(this GrpcDurableTaskWorkerOptions options, TimeSpan deadline)
    {
        options.Internal.HelloDeadline = deadline;
    }

    /// <summary>
    /// Sets the silent-disconnect timeout. If no message (including health pings) arrives on the
    /// work-item stream within this window, the worker treats the stream as silently disconnected
    /// and reconnects.
    /// </summary>
    /// <param name="options">The gRPC worker options.</param>
    /// <param name="timeout">The timeout; non-positive disables silent-disconnect detection.</param>
    /// <remarks>
    /// This is an internal API that supports the DurableTask infrastructure and not subject to
    /// the same compatibility standards as public APIs. It may be changed or removed without notice in
    /// any release.
    /// </remarks>
    public static void SetSilentDisconnectTimeout(this GrpcDurableTaskWorkerOptions options, TimeSpan timeout)
    {
        options.Internal.SilentDisconnectTimeout = timeout;
    }
}
