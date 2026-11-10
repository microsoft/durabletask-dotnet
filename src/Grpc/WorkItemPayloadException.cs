// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask;

/// <summary>
/// A payload resolution failure after a successful transport read.
/// </summary>
/// <param name="innerException">The payload resolution failure.</param>
/// <param name="permanentFailure">Safe failure details when the payload is confirmed missing.</param>
internal class PayloadResolutionException(Exception innerException, P.TaskFailureDetails? permanentFailure)
    : Exception("Failed to resolve external payloads.", innerException)
{
    /// <summary>Gets failure details for a confirmed permanent failure, or null for a retryable failure.</summary>
    public P.TaskFailureDetails? PermanentFailure { get; } = permanentFailure;
}

/// <summary>Identifies a failed delivery that must not be dispatched.</summary>
/// <param name="workItem">The failed delivery, including its original completion token.</param>
/// <param name="innerException">The payload resolution failure.</param>
/// <param name="permanentFailure">Safe failure details when the payload is confirmed missing.</param>
internal sealed class WorkItemPayloadException(
    P.WorkItem workItem, Exception innerException, P.TaskFailureDetails? permanentFailure)
    : PayloadResolutionException(innerException, permanentFailure)
{
    /// <summary>
    /// Gets the failed delivery. Its payload fields may be partially resolved.
    /// </summary>
    public P.WorkItem WorkItem { get; } = workItem;
}

/// <summary>Exposes transport-only reads for recovery that needs history metadata, not payload values.</summary>
/// <typeparam name="T">The response type.</typeparam>
internal interface IPayloadStreamReader<T>
{
    /// <summary>Gets the underlying reader without payload resolution.</summary>
    IAsyncStreamReader<T> Inner { get; }
}
