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
}
