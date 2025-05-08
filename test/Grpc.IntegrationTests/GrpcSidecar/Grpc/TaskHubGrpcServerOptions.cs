// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Sidecar.Grpc;

/// <summary>
/// Options for configuring the task hub gRPC server.
/// </summary>
public class TaskHubGrpcServerOptions
{
    /// <summary>
    /// The high-level mode of operation for the gRPC server.
    /// </summary>
    public TaskHubGrpcServerMode Mode { get; set; }
}

/// <summary>
/// A set of options that determine what capabilities are enabled for the gRPC server.
/// </summary>
public enum TaskHubGrpcServerMode
{
    /// <summary>
    /// The gRPC server handles both orchestration dispatching and management API requests.
    /// </summary>
    ApiServerAndDispatcher,

    /// <summary>
    /// The gRPC server handles management API requests but not orchestration dispatching.
    /// </summary>
    ApiServerOnly,
}