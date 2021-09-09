//  ----------------------------------------------------------------------------------
//  Copyright Microsoft Corporation
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//  http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//  ----------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace DurableTask.Grpc;

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
