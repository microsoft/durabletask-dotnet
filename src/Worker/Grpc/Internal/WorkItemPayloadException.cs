// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc.Internal;

/// <summary>Allows a worker extension to separate transport and payload cancellation on a work-item stream.</summary>
/// <remarks>
/// Infrastructure contract used by payload extensions. This is not a supported application API.
/// </remarks>
public interface IWorkItemStreamReader
{
    /// <summary>Receives and hydrates the next delivery using separate cancellation boundaries.</summary>
    /// <param name="onMessageReceived">Invoked after a successful transport read, before payload resolution.</param>
    /// <param name="readCancellation">Cancellation for the underlying transport read.</param>
    /// <param name="payloadCancellation">Worker cancellation for payload resolution.</param>
    /// <returns>Whether a fully hydrated delivery is available.</returns>
    Task<bool> MoveNextAsync(Action? onMessageReceived, CancellationToken readCancellation, CancellationToken payloadCancellation);
}

/// <summary>
/// A received delivery whose payloads could not be resolved. The item is only safe to abandon, not dispatch.
/// </summary>
/// <param name="workItem">The failed delivery, including its original completion token.</param>
/// <param name="innerException">The payload resolution failure.</param>
/// <remarks>
/// Infrastructure contract between worker extensions and the gRPC worker. This is not a supported application API.
/// </remarks>
public sealed class WorkItemPayloadException(P.WorkItem workItem, Exception innerException)
    : Exception("Failed to resolve payloads for a work-item delivery.", innerException)
{
    /// <summary>
    /// Gets the failed delivery. Its payload fields may be partially resolved.
    /// </summary>
    public P.WorkItem WorkItem { get; } = workItem;
}
