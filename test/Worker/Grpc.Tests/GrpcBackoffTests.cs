// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

public class GrpcBackoffTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Compute_ZeroBase_ReturnsZero(bool fullJitter)
    {
        // Arrange
        Random random = new(42);

        // Act
        TimeSpan delay = GrpcBackoff.Compute(attempt: 5, baseDelay: TimeSpan.Zero, cap: TimeSpan.FromSeconds(30), random, fullJitter);

        // Assert
        delay.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Compute_NegativeBase_ReturnsZero(bool fullJitter)
    {
        // Arrange
        Random random = new(42);

        // Act
        TimeSpan delay = GrpcBackoff.Compute(attempt: 0, baseDelay: TimeSpan.FromMilliseconds(-100), cap: TimeSpan.FromSeconds(30), random, fullJitter);

        // Assert
        delay.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Compute_NonPositiveCap_ReturnsZero(bool fullJitter)
    {
        // Arrange
        DeterministicRandom random = new(0.999999);

        // Act
        TimeSpan zero = GrpcBackoff.Compute(attempt: 3, baseDelay: TimeSpan.FromSeconds(1), cap: TimeSpan.Zero, random, fullJitter);
        TimeSpan negative = GrpcBackoff.Compute(attempt: 3, baseDelay: TimeSpan.FromSeconds(1), cap: TimeSpan.FromSeconds(-1), random, fullJitter);

        // Assert
        zero.Should().Be(TimeSpan.Zero);
        negative.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Compute_FullJitter_NeverExceedsCap()
    {
        // Arrange
        TimeSpan cap = TimeSpan.FromSeconds(30);
        TimeSpan baseDelay = TimeSpan.FromSeconds(1);
        Random random = new(1);

        // Act + Assert: try a wide range of attempts, including pathological values.
        // Note: this invariant is full-jitter-specific — biased mode intentionally returns up to
        // 1.2x the upper bound and so can legally exceed the cap.
        for (int attempt = 0; attempt < 50; attempt++)
        {
            TimeSpan delay = GrpcBackoff.Compute(attempt, baseDelay, cap, random, fullJitter: true);
            delay.Should().BeLessThanOrEqualTo(cap, $"attempt {attempt} produced {delay}");
            delay.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        }
    }

    [Fact]
    public void Compute_FullJitter_GrowsExponentiallyUntilCap()
    {
        // Arrange: a Random that always returns ~1.0 forces the upper bound of the jitter window.
        DeterministicRandom random = new(value: 0.999999);
        TimeSpan baseDelay = TimeSpan.FromSeconds(1);
        TimeSpan cap = TimeSpan.FromSeconds(30);

        // Act
        double d0 = GrpcBackoff.Compute(0, baseDelay, cap, random, fullJitter: true).TotalMilliseconds;
        double d1 = GrpcBackoff.Compute(1, baseDelay, cap, random, fullJitter: true).TotalMilliseconds;
        double d2 = GrpcBackoff.Compute(2, baseDelay, cap, random, fullJitter: true).TotalMilliseconds;
        double d3 = GrpcBackoff.Compute(3, baseDelay, cap, random, fullJitter: true).TotalMilliseconds;
        double d10 = GrpcBackoff.Compute(10, baseDelay, cap, random, fullJitter: true).TotalMilliseconds;

        // Assert: roughly doubles each step until cap is reached.
        d0.Should().BeApproximately(1000, 1);
        d1.Should().BeApproximately(2000, 1);
        d2.Should().BeApproximately(4000, 1);
        d3.Should().BeApproximately(8000, 1);
        d10.Should().BeApproximately(30000, 1, "should be clamped at the cap");
    }

    [Fact]
    public void Compute_FullJitter_StaysWithinBounds()
    {
        // Arrange: with random=0 the result is 0; with random=1 the result is the bound.
        TimeSpan baseDelay = TimeSpan.FromSeconds(1);
        TimeSpan cap = TimeSpan.FromSeconds(30);

        // Act + Assert: random=0 → 0
        TimeSpan low = GrpcBackoff.Compute(3, baseDelay, cap, new DeterministicRandom(0.0), fullJitter: true);
        low.TotalMilliseconds.Should().BeApproximately(0, 0.5);

        // random ~1 → bound (= 8s for attempt=3, base=1s)
        TimeSpan high = GrpcBackoff.Compute(3, baseDelay, cap, new DeterministicRandom(0.999999), fullJitter: true);
        high.TotalMilliseconds.Should().BeApproximately(8000, 1);
    }

    [Fact]
    public void Compute_BiasedJitter_StaysWithinBounds()
    {
        // Arrange: biased jitter returns a value in [upperBound, upperBound * 1.2].
        // attempt=3, base=1s → upperBound=8s.
        TimeSpan baseDelay = TimeSpan.FromSeconds(1);
        TimeSpan cap = TimeSpan.FromSeconds(30);

        // Act + Assert: random=0 → upperBound (lower edge of biased window).
        TimeSpan low = GrpcBackoff.Compute(3, baseDelay, cap, new DeterministicRandom(0.0), fullJitter: false);
        low.TotalMilliseconds.Should().BeApproximately(8000, 1);

        // random ~1 → upperBound * 1.2 (upper edge).
        TimeSpan high = GrpcBackoff.Compute(3, baseDelay, cap, new DeterministicRandom(0.999999), fullJitter: false);
        high.TotalMilliseconds.Should().BeApproximately(9600, 1);

        // mid value → halfway.
        TimeSpan mid = GrpcBackoff.Compute(3, baseDelay, cap, new DeterministicRandom(0.5), fullJitter: false);
        mid.TotalMilliseconds.Should().BeApproximately(8800, 1);
    }

    [Fact]
    public void Compute_NegativeAttempt_TreatedAsZero()
    {
        // Arrange
        DeterministicRandom random = new(0.999999);

        // Act
        TimeSpan delay = GrpcBackoff.Compute(attempt: -5, baseDelay: TimeSpan.FromSeconds(1), cap: TimeSpan.FromSeconds(30), random, fullJitter: true);

        // Assert
        delay.TotalMilliseconds.Should().BeApproximately(1000, 1);
    }

    [Fact]
    public void Compute_FullJitter_CapSmallerThanBase_ClampsToCap()
    {
        // Arrange: cap is intentionally smaller than baseDelay; the cap must still be honored.
        // Note: biased mode would return up to 1.2 * cap here by design, so this invariant is
        // full-jitter-only.
        DeterministicRandom random = new(0.999999);
        TimeSpan baseDelay = TimeSpan.FromSeconds(5);
        TimeSpan cap = TimeSpan.FromSeconds(1);

        // Act
        TimeSpan delay = GrpcBackoff.Compute(attempt: 3, baseDelay, cap, random, fullJitter: true);

        // Assert: with random ~ 1 the result is the bound, which must equal the cap.
        delay.TotalMilliseconds.Should().BeApproximately(1000, 1);
        delay.Should().BeLessThanOrEqualTo(cap);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Compute_AttemptIsCappedAt30(bool fullJitter)
    {
        // Arrange: pick a base/cap where the cap is large enough that 2^30 * base does not saturate it,
        // so the exponent — not the cap — drives the upper bound. This lets us observe the internal
        // attempt clamp at 30: any attempt ≥ 30 must yield the same upper bound as attempt = 30.
        TimeSpan baseDelay = TimeSpan.FromMilliseconds(1);
        TimeSpan cap = TimeSpan.FromDays(365); // 2^30 ms ≈ 12.4 days < cap.

        // Act: produce a fresh DeterministicRandom for each call so the same NextDouble() value is
        // sampled
        TimeSpan at30 = GrpcBackoff.Compute(30, baseDelay, cap, new DeterministicRandom(1.0), fullJitter);
        TimeSpan at31 = GrpcBackoff.Compute(31, baseDelay, cap, new DeterministicRandom(1.0), fullJitter);
        TimeSpan at100 = GrpcBackoff.Compute(100, baseDelay, cap, new DeterministicRandom(1.0), fullJitter);
        TimeSpan atIntMax = GrpcBackoff.Compute(int.MaxValue, baseDelay, cap, new DeterministicRandom(1.0), fullJitter);

        // Assert: all produce the same delay, equal to the attempt=30 value (sanity-checked against
        // the analytical upper bound of 2^30 ms — exact for full jitter at random=1, and 2^30 * 1.2
        // for biased mode at random=1).
        double expectedUpperBoundMs = Math.Pow(2, 30); // 2^30 ms
        if (fullJitter)
        {
            // random = 1 → result == upper bound
            at30.TotalMilliseconds.Should().BeApproximately(expectedUpperBoundMs, 1);
        }
        else
        {
            // random = 1 → result == upper bound * 1.2
            at30.TotalMilliseconds.Should().BeApproximately(expectedUpperBoundMs * 1.2, 1);
        }

        at31.Should().Be(at30);
        at100.Should().Be(at30);
        atIntMax.Should().Be(at30);
    }

    sealed class DeterministicRandom : Random
    {
        readonly double value;

        public DeterministicRandom(double value)
        {
            this.value = value;
        }

        public override double NextDouble() => this.value;
    }
}
