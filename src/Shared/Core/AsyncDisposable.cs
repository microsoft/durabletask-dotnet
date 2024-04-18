// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// A struct for calling a simple delegate on dispose.
/// </summary>
/// <param name="callback">The callback to invoke on disposal.</param>
struct AsyncDisposable(Func<ValueTask> callback) : IAsyncDisposable
{
    Func<ValueTask>? callback = callback;

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        Func<ValueTask>? callback = Interlocked.Exchange(ref this.callback, null);
        return callback?.Invoke() ?? default;
    }
}
