// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

public class ReconnectBackoffTests
{
    [Fact]
    public void Compute_ZeroBase_ReturnsZero()
    {
        // Arrange
        Random random = new(42);

        // Act
        TimeSpan delay = ReconnectBackoff.Compute(attempt: 5, baseDelay: TimeSpan.Zero, cap: TimeSpan.FromSeconds(30), random);

        // Assert
        delay.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Compute_NegativeBase_ReturnsZero()
    {
        // Arrange
        Random random = new(42);

        // Act
        TimeSpan delay = ReconnectBackoff.Compute(attempt: 0, baseDelay: TimeSpan.FromMilliseconds(-100), cap: TimeSpan.FromSeconds(30), random);

        // Assert
        delay.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Compute_NeverExceedsCap()
    {
        // Arrange
        TimeSpan cap = TimeSpan.FromSeconds(30);
        TimeSpan baseDelay = TimeSpan.FromSeconds(1);
        Random random = new(1);

        // Act + Assert: try a wide range of attempts, including pathological values.
        for (int attempt = 0; attempt < 50; attempt++)
        {
            TimeSpan delay = ReconnectBackoff.Compute(attempt, baseDelay, cap, random);
            delay.Should().BeLessThanOrEqualTo(cap, $"attempt {attempt} produced {delay}");
            delay.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        }
    }

    [Fact]
    public void Compute_GrowsExponentiallyUntilCap()
    {
        // Arrange: a Random that always returns 1.0 forces the upper bound of the jitter window.
        DeterministicRandom random = new(value: 0.999999);
        TimeSpan baseDelay = TimeSpan.FromSeconds(1);
        TimeSpan cap = TimeSpan.FromSeconds(30);

        // Act
        double d0 = ReconnectBackoff.Compute(0, baseDelay, cap, random).TotalMilliseconds;
        double d1 = ReconnectBackoff.Compute(1, baseDelay, cap, random).TotalMilliseconds;
        double d2 = ReconnectBackoff.Compute(2, baseDelay, cap, random).TotalMilliseconds;
        double d3 = ReconnectBackoff.Compute(3, baseDelay, cap, random).TotalMilliseconds;
        double d10 = ReconnectBackoff.Compute(10, baseDelay, cap, random).TotalMilliseconds;

        // Assert: roughly doubles each step until cap is reached.
        d0.Should().BeApproximately(1000, 1);
        d1.Should().BeApproximately(2000, 1);
        d2.Should().BeApproximately(4000, 1);
        d3.Should().BeApproximately(8000, 1);
        d10.Should().BeApproximately(30000, 1, "should be clamped at the cap");
    }

    [Fact]
    public void Compute_WithFullJitter_StaysWithinBounds()
    {
        // Arrange: with random=0 the result is 0; with random=1 the result is the bound.
        TimeSpan baseDelay = TimeSpan.FromSeconds(1);
        TimeSpan cap = TimeSpan.FromSeconds(30);

        // Act + Assert: random=0 → 0
        TimeSpan low = ReconnectBackoff.Compute(3, baseDelay, cap, new DeterministicRandom(0.0));
        low.TotalMilliseconds.Should().BeApproximately(0, 0.5);

        // random ~1 → bound (= 8s for attempt=3, base=1s)
        TimeSpan high = ReconnectBackoff.Compute(3, baseDelay, cap, new DeterministicRandom(0.999999));
        high.TotalMilliseconds.Should().BeApproximately(8000, 1);
    }

    [Fact]
    public void Compute_NegativeAttempt_TreatedAsZero()
    {
        // Arrange
        DeterministicRandom random = new(0.999999);

        // Act
        TimeSpan delay = ReconnectBackoff.Compute(attempt: -5, baseDelay: TimeSpan.FromSeconds(1), cap: TimeSpan.FromSeconds(30), random);

        // Assert
        delay.TotalMilliseconds.Should().BeApproximately(1000, 1);
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
