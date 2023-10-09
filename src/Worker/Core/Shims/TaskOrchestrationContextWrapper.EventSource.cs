// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using DurableTask.Core;

namespace Microsoft.DurableTask.Worker.Shims;

/// <summary>
/// A wrapper to go from <see cref="OrchestrationContext" /> to <see cref="TaskOrchestrationContext "/>.
/// </summary>
sealed partial class TaskOrchestrationContextWrapper
{
    /// <summary>
    /// Event source contract.
    /// </summary>
    interface IEventSource
    {
        /// <summary>
        /// Gets the type of the event stored in the completion source.
        /// </summary>
        Type EventType { get; }

        /// <summary>
        /// Tries to set the result on tcs.
        /// </summary>
        /// <param name="result">The result.</param>
        void TrySetResult(object result);
    }

    class EventTaskCompletionSource<T> : TaskCompletionSource<T>, IEventSource
    {
        /// <inheritdoc/>
        public Type EventType => typeof(T);

        /// <inheritdoc/>
        void IEventSource.TrySetResult(object result) => this.TrySetResult((T)result);
    }

    class NamedQueue<TValue>
    {
        readonly Dictionary<string, Queue<TValue>> buffers = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string name, TValue value)
        {
            if (!this.buffers.TryGetValue(name, out Queue<TValue>? queue))
            {
                queue = new Queue<TValue>();
                this.buffers[name] = queue;
            }

            queue.Enqueue(value);
        }

        public bool TryTake(string name, [NotNullWhen(true)] out TValue? value)
        {
            if (this.buffers.TryGetValue(name, out Queue<TValue>? queue))
            {
                value = queue.Dequeue()!;
                if (queue.Count == 0)
                {
                    this.buffers.Remove(name);
                }

                return true;
            }

            value = default;
            return false;
        }

        public IEnumerable<(string EventName, TValue EventPayload)> TakeAll()
        {
            foreach ((string eventName, Queue<TValue> eventPayloads) in this.buffers)
            {
                foreach (TValue payload in eventPayloads)
                {
                    yield return (eventName, payload);
                }
            }

            this.buffers.Clear();
        }
    }
}
