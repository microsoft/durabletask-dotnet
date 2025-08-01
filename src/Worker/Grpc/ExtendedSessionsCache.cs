// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.DurableTask.Worker.Grpc;

public class ExtendedSessionsCache
{
    private IMemoryCache? extendedSessions;

    internal IMemoryCache GetOrInitializeCache(double extendedSessionIdleTimeoutInSeconds)
    {
        this.extendedSessions ??= new MemoryCache(new MemoryCacheOptions
        {
            ExpirationScanFrequency = TimeSpan.FromSeconds(extendedSessionIdleTimeoutInSeconds),
        });

        return this.extendedSessions;
    }
}
