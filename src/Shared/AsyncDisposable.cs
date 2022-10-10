// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// A struct for calling a simple delegate on dispose.
/// </summary>
struct AsyncDisposable : IAsyncDisposable
{
    Func<ValueTask>? callback;

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncDisposable"/> struct.
    /// </summary>
    /// <param name="callback">The callback to invoke on disposal.</param>
    public AsyncDisposable(Func<ValueTask> callback)
    {
        this.callback = callback;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncDisposable"/> struct.
    /// </summary>
    public AsyncDisposable()
    {
        this.callback = null;
    }

    /// <summary>
    /// Gets the empty async disposable.
    /// </summary>
    public static AsyncDisposable Empty { get; } = default;

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        Func<ValueTask>? callback = Interlocked.Exchange(ref this.callback, null);
        return callback?.Invoke() ?? default;
    }
}
