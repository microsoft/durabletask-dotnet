// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Dapr.DurableTask;

public static class RandomExtensions
{
#if NET6_0_OR_GREATER
    public static DateTimeOffset NextDateTimeOffset(this Random random, DateTimeOffset min, TimeSpan max)
    {
        ArgumentNullException.ThrowIfNull(random);
        bool negative = max.Ticks < 0;
        TimeSpan range = TimeSpan.FromTicks(Random.Shared.NextInt64(0, Math.Abs(max.Ticks)));
        return negative ? min - range : min + range;
    }

    public static DateTimeOffset NextDateTimeOffset(this Random random, TimeSpan max)
        => random.NextDateTimeOffset(DateTimeOffset.UtcNow, max);

    public static DateTime NextDateTime(this Random random, DateTime min, TimeSpan max)
    {
        ArgumentNullException.ThrowIfNull(random);
        bool negative = max.Ticks < 0;
        TimeSpan range = TimeSpan.FromTicks(Random.Shared.NextInt64(0, Math.Abs(max.Ticks)));
        return negative ? min - range : min + range;
    }

    public static DateTime NextDateTime(this Random random, TimeSpan max)
        => random.NextDateTime(DateTime.UtcNow, max);
#endif
}