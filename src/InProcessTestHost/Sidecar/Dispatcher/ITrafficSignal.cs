// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Sidecar.Dispatcher;

/// <summary>
/// A simple primitive that can be used to block logical threads until some condition occurs.
/// </summary>
interface ITrafficSignal
{
    /// <summary>
    /// Gets provides a human-friendly reason for why the signal is in the "wait" state.
    /// </summary>
    string WaitReason { get; }

    /// <summary>
    /// Blocks the caller until the Set method is called.
    /// </summary>
    /// <param name="waitTime">The amount of time to wait until the signal is unblocked.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to interrupt a waiting caller.</param>
    /// <returns>
    /// Returns <c>true</c> if the traffic signal is all-clear; <c>false</c> if we timed-out waiting for the signal to clear.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown if <paramref name="cancellationToken"/> is triggered while waiting.
    /// </exception>
    Task<bool> WaitAsync(TimeSpan waitTime, CancellationToken cancellationToken);
}
