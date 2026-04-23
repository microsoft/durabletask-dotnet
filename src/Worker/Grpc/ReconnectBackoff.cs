// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Security.Cryptography;

namespace Microsoft.DurableTask.Worker.Grpc;

/// <summary>
/// Helpers for computing reconnect backoff delays in the gRPC worker.
/// </summary>
static class ReconnectBackoff
{
    /// <summary>
    /// Creates a random source for reconnect jitter using an explicit random seed so multiple workers on
    /// older runtimes don't converge on the same time-based seed.
    /// </summary>
    /// <returns>A random source suitable for reconnect jitter.</returns>
    public static Random CreateRandom()
    {
        byte[] seedBytes = new byte[sizeof(int)];
        using RandomNumberGenerator randomNumberGenerator = RandomNumberGenerator.Create();
        randomNumberGenerator.GetBytes(seedBytes);
        return new Random(BitConverter.ToInt32(seedBytes, 0));
    }

    /// <summary>
    /// Computes a full-jitter exponential backoff delay: a uniformly random TimeSpan in
    /// <c>[0, min(cap, base * 2^attempt)]</c>. Returns <see cref="TimeSpan.Zero"/> when
    /// <paramref name="baseDelay"/> or <paramref name="cap"/> is non-positive.
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

        double capMs = Math.Max(0, cap.TotalMilliseconds);
        double exponentialMs = baseDelay.TotalMilliseconds * Math.Pow(2, safeAttempt);
        double upperBoundMs = Math.Min(capMs, exponentialMs);

        // Full jitter intentionally allows any value in the retry window. The wide spread keeps many
        // workers that saw the same outage from reconnecting in lockstep against the backend.
        double jitteredMs = random.NextDouble() * upperBoundMs;
        return TimeSpan.FromMilliseconds(jitteredMs);
    }
}
