// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DurableTask.Grpc;

sealed class AsyncLock : IDisposable
{
    readonly SemaphoreSlim semaphore = new(initialCount: 1);

    public AsyncLock()
    {
    }

    public async Task<Releaser> AcquireAsync()
    {
        await this.semaphore.WaitAsync();
        return new Releaser(this);
    }

    public void Release()
    {
        this.semaphore.Release();
    }

    public void Dispose()
    {
        this.semaphore.Dispose();
    }

    public struct Releaser : IDisposable
    {
        readonly AsyncLock asyncLock;

        public Releaser(AsyncLock asyncLock)
        {
            this.asyncLock = asyncLock;
        }

        public void Dispose()
        {
            this.asyncLock.Release();
        }
    }
}
