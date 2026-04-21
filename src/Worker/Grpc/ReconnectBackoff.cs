// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.Grpc;

/// <summary>
/// Helpers for computing reconnect backoff delays in the gRPC worker.
/// </summary>
static class ReconnectBackoff
{
    /// <summary>
    /// Computes a full-jitter exponential backoff delay: a uniformly random TimeSpan in
    /// <c>[0, min(cap, base * 2^attempt)]</c>. Returns <see cref="TimeSpan.Zero"/> when the base delay
    /// is non-positive.
    /// </summary>
    /// <param name="attempt">The retry attempt index, starting at 0.</param>
    /// <param name="baseDelay">The base delay used for the exponential growth.</param>
    /// <param name="cap">The maximum delay before jitter is applied.</param>
    /// <param name="random">The random source used for jitter.</param>
    /// <returns>The computed jittered delay.</returns>
    public static TimeSpan Compute(int attempt, TimeSpan baseDelay, TimeSpan cap, Random random)
    {
        if (baseDelay <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        if (attempt < 0)
        {
            attempt = 0;
        }

        // Cap the exponent to avoid overflow in 2^attempt for pathological attempt values.
        int safeAttempt = Math.Min(attempt, 30);

        double capMs = Math.Max(baseDelay.TotalMilliseconds, cap.TotalMilliseconds);
        double exp = baseDelay.TotalMilliseconds * Math.Pow(2, safeAttempt);
        double bound = Math.Min(capMs, exp);
        double jittered = random.NextDouble() * bound;
        return TimeSpan.FromMilliseconds(jittered);
    }
}
