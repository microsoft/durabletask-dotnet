// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using DurableTask.Core;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask;

partial class TaskOrchestrationShim
{
    sealed class TaskOrchestrationContextWrapper : TaskOrchestrationContext
    {
        readonly Dictionary<string, IEventSource> externalEventSources = new(StringComparer.OrdinalIgnoreCase);
        readonly NamedQueue<string> externalEventBuffer = new();
        readonly Queue<Action> localActivityCalls = new();

        readonly OrchestrationContext innerContext;
        readonly TaskName name;
        readonly WorkerContext workerContext;
        readonly OrchestrationRuntimeState runtimeState;
        readonly ILogger orchestratorLogger;
        readonly object? deserializedInput;

        int newGuidCounter;
        object? customStatus;

        public TaskOrchestrationContextWrapper(
            OrchestrationContext innerContext,
            TaskName name,
            WorkerContext workerContext,
            OrchestrationRuntimeState runtimeState,
            object? deserializedInput)
        {
            this.innerContext = innerContext;
            this.name = name;
            this.workerContext = workerContext;
            this.runtimeState = runtimeState;
            this.orchestratorLogger = this.CreateReplaySafeLogger(workerContext.Logger);
            this.deserializedInput = deserializedInput;
        }

        public override TaskName Name => this.name;

        public override string InstanceId => this.innerContext.OrchestrationInstance.InstanceId;
        public override ParentInstance? Parent => this.runtimeState.ExecutionStartedEvent?.ParentInstance;

        public override bool IsReplaying => this.innerContext.IsReplaying;

        public override DateTime CurrentUtcDateTime => this.innerContext.CurrentUtcDateTime;

        public override T GetInput<T>() => (T)this.deserializedInput!;

        public override async Task<T> CallActivityAsync<T>(
            TaskName name,
            object? input = null,
            TaskOptions? options = null)
        {
            // Since the input parameter takes any object, it's possible that callers may accidentally provide a TaskOptions parameter here
            // when the actually meant to provide TaskOptions for the optional options parameter.
            if (input is TaskOptions && options == null)
            {
                throw new ArgumentException(
                    $"A {nameof(TaskOptions)} value was provided for the activity input but no value was provided for {nameof(options)}. " + 
                    $"Did you actually mean to provide a {nameof(TaskOptions)} value for the {nameof(options)} parameter?",
                    nameof(input));
            }

            try
            {
                // TODO: Cancellation (https://github.com/microsoft/durabletask-dotnet/issues/7)
                // TODO: DataConverter?

                if (options?.RetryPolicy != null)
                {
                    return await this.innerContext.ScheduleWithRetry<T>(
                        name.Name,
                        name.Version,
                        options.RetryPolicy.ToDurableTaskCoreRetryOptions(),
                        input);
                }
                else if (options?.RetryHandler != null)
                {
                    return await this.InvokeWithCustomRetryHandler(
                        () => this.innerContext.ScheduleTask<T>(name.Name, name.Version, input),
                        name.Name,
                        options.RetryHandler,
                        options.CancellationToken);
                }
                else
                {
                    return await this.innerContext.ScheduleTask<T>(name.Name, name.Version, input);
                }

            }
            catch (global::DurableTask.Core.Exceptions.TaskFailedException e)
            {
                // Hide the core DTFx types and instead use our own
                throw new TaskFailedException(name, e.ScheduleId, e);
            }
        }

        [Obsolete("This method is not yet fully implemented")]
        public override Task<T> CallActivityAsync<T>(Func<object?, T> activityLambda, object? input = null, TaskOptions? options = null)
        {
            if (options != null)
            {
                throw new NotImplementedException($"{nameof(TaskOptions)} are not yet supported.");
            }

            TaskCompletionSource<T> tcs = new();
            this.localActivityCalls.Enqueue(() =>
            {
                try
                {
                    T output = activityLambda(input);
                    tcs.SetResult(output);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            return tcs.Task;
        }

        internal void ExecuteLocalActivityCalls()
        {
            while (this.localActivityCalls.Count > 0)
            {
                Action localActivityLambda = this.localActivityCalls.Dequeue();

                // Exceptions are never expected to escape here
                localActivityLambda.Invoke();
            }
        }

        public override async Task<TResult> CallSubOrchestratorAsync<TResult>(
            TaskName orchestratorName,
            string? instanceId = null,
            object? input = null,
            TaskOptions? options = null)
        {
            // TODO: Check to see if this orchestrator is defined

            // TODO: IDataConverter

            instanceId ??= this.NewGuid().ToString("N");

            try
            {
                if (options?.RetryPolicy != null)
                {
                    return await this.innerContext.CreateSubOrchestrationInstanceWithRetry<TResult>(
                        orchestratorName.Name,
                        orchestratorName.Version,
                        options.RetryPolicy.ToDurableTaskCoreRetryOptions(),
                        input);
                }
                else if (options?.RetryHandler != null)
                {
                    return await this.InvokeWithCustomRetryHandler(
                        () => this.innerContext.CreateSubOrchestrationInstance<TResult>(
                            orchestratorName.Name,
                            orchestratorName.Version,
                            instanceId,
                            input),
                        orchestratorName.Name,
                        options.RetryHandler,
                        options.CancellationToken);
                }
                else
                {
                    return await this.innerContext.CreateSubOrchestrationInstance<TResult>(
                        orchestratorName.Name,
                        orchestratorName.Version,
                        instanceId,
                        input);
                }
            }
            catch (global::DurableTask.Core.Exceptions.SubOrchestrationFailedException e)
            {
                // Hide the core DTFx types and instead use our own
                throw new TaskFailedException(orchestratorName, e.ScheduleId, e);
            }
        }

        public override async Task CreateTimer(DateTime fireAt, CancellationToken cancellationToken)
        {
            // Make sure we're always operating in UTC
            DateTime finalFireAtUtc = fireAt.ToUniversalTime();

            // Longer timers are broken down into smaller timers. For example, if fireAt is 7 days from now
            // and the max interval is 3 days, there will be two 3-day timers and a single one-day timer.
            // This is primarily to support backends that don't support infinite timers, like Azure Storage.
            TimeSpan maximumTimerInterval = this.workerContext.TimerOptions.MaximumTimerInterval;
            TimeSpan remainingTime = finalFireAtUtc.Subtract(this.CurrentUtcDateTime);
            while (remainingTime > maximumTimerInterval && !cancellationToken.IsCancellationRequested)
            {
                DateTime nextFireAt = this.CurrentUtcDateTime.Add(maximumTimerInterval);
                await this.innerContext.CreateTimer<object>(nextFireAt, state: null!, cancellationToken);
                remainingTime = finalFireAtUtc.Subtract(this.CurrentUtcDateTime);
            }

            await this.innerContext.CreateTimer<object>(finalFireAtUtc, state: null!, cancellationToken);
        }

        public override Task<T> WaitForExternalEvent<T>(string eventName, CancellationToken cancellationToken = default)
        {
            // Return immediately if this external event has already arrived.
            if (this.externalEventBuffer.TryTake(eventName, out string? bufferedEventPayload))
            {
                return Task.FromResult(this.workerContext.DataConverter.Deserialize<T>(bufferedEventPayload));
            }

            // Create a task completion source that will be set when the external event arrives.
            EventTaskCompletionSource<T> eventSource = new();
            if (this.externalEventSources.TryGetValue(eventName, out IEventSource? existing))
            {
                if (existing.EventType != typeof(T))
                {
                    throw new ArgumentException($"Events with the same name must have the same type argument. Expected {existing.EventType.FullName}.");
                }

                existing.Next = eventSource;
            }
            else
            {
                this.externalEventSources.Add(eventName, eventSource);
            }

            cancellationToken.Register(() => eventSource.TrySetCanceled(cancellationToken));
            return eventSource.Task;
        }

        internal void CompleteExternalEvent(string eventName, string rawEventPayload)
        {
            if (this.externalEventSources.TryGetValue(eventName, out IEventSource? waiter))
            {
                object? value = this.workerContext.DataConverter.Deserialize(rawEventPayload, waiter.EventType);

                // Events are completed in FIFO order. Remove the key if the last event was delivered.
                if (waiter.Next == null)
                {
                    this.externalEventSources.Remove(eventName);
                }
                else
                {
                    this.externalEventSources[eventName] = waiter.Next;
                }

                waiter.TrySetResult(value);
            }
            else
            {
                // The orchestrator isn't waiting for this event (yet?). Save it in case
                // the orchestrator wants it later.
                this.externalEventBuffer.Add(eventName, rawEventPayload);
            }
        }

        public override void SetCustomStatus(object? customStatus)
        {
            this.customStatus = customStatus;
        }

        /// <inheritdoc/>
        public override void ContinueAsNew(object newInput, bool preserveUnprocessedEvents = true)
        {
            this.innerContext.ContinueAsNew(newInput);

            if (preserveUnprocessedEvents)
            {
                // Send all the buffered external events to ourself.
                OrchestrationInstance instance = new() { InstanceId = this.InstanceId };
                foreach ((string eventName, string eventPayload) in this.externalEventBuffer.TakeAll())
                {
                    this.innerContext.SendEvent(instance, eventName, eventPayload);
                }
            }
        }

        public override Guid NewGuid()
        {
            static void SwapByteArrayValues(byte[] byteArray)
            {
                SwapByteArrayElements(byteArray, 0, 3);
                SwapByteArrayElements(byteArray, 1, 2);
                SwapByteArrayElements(byteArray, 4, 5);
                SwapByteArrayElements(byteArray, 6, 7);
            }

            static void SwapByteArrayElements(byte[] byteArray, int left, int right)
            {
                byte temp = byteArray[left];
                byteArray[left] = byteArray[right];
                byteArray[right] = temp;
            }

            const string DnsNamespaceValue = "9e952958-5e33-4daf-827f-2fa12937b875";
            const int DeterministicGuidVersion = 5;

            Guid namespaceValueGuid = Guid.Parse(DnsNamespaceValue);

            // The name is a combination of the instance ID, the current orchestrator date/time, and a counter.
            string guidNameValue = string.Concat(
                this.InstanceId,
                "_",
                this.CurrentUtcDateTime.ToString("o"),
                "_",
                this.newGuidCounter.ToString());

            this.newGuidCounter++;

            byte[] nameByteArray = Encoding.UTF8.GetBytes(guidNameValue);
            byte[] namespaceValueByteArray = namespaceValueGuid.ToByteArray();
            SwapByteArrayValues(namespaceValueByteArray);

            byte[] hashByteArray;
            using (HashAlgorithm hashAlgorithm = SHA1.Create())
            {
                hashAlgorithm.TransformBlock(namespaceValueByteArray, 0, namespaceValueByteArray.Length, null, 0);
                hashAlgorithm.TransformFinalBlock(nameByteArray, 0, nameByteArray.Length);
                hashByteArray = hashAlgorithm.Hash;
            }

            byte[] newGuidByteArray = new byte[16];
            Array.Copy(hashByteArray, 0, newGuidByteArray, 0, 16);

            int versionValue = DeterministicGuidVersion;
            newGuidByteArray[6] = (byte)((newGuidByteArray[6] & 0x0F) | (versionValue << 4));
            newGuidByteArray[8] = (byte)((newGuidByteArray[8] & 0x3F) | 0x80);

            SwapByteArrayValues(newGuidByteArray);

            return new Guid(newGuidByteArray);
        }

        internal string? GetDeserializedCustomStatus()
        {
            return this.workerContext.DataConverter.Serialize(this.customStatus);
        }

        async Task<T> InvokeWithCustomRetryHandler<T>(
            Func<Task<T>> action,
            string taskName,
            AsyncRetryHandler retryHandler,
            CancellationToken cancellationToken)
        {
            DateTime startTime = this.CurrentUtcDateTime;
            int failureCount = 0;

            while (true)
            {
                try
                {
                    return await action();
                }
                catch (global::DurableTask.Core.Exceptions.OrchestrationException e)
                {
                    // Some failures are not retriable, like failures for missing activities or sub-orchestrations
                    if (e.FailureDetails?.IsNonRetriable == true)
                    {
                        throw;
                    }

                    failureCount++;

                    this.orchestratorLogger.RetryingTask(
                        this.InstanceId,
                        taskName,
                        attempt: failureCount);

                    RetryContext retryContext = new(
                        this,
                        failureCount,
                        TaskFailureDetails.FromCoreException(e),
                        this.CurrentUtcDateTime.Subtract(startTime),
                        cancellationToken);

                    bool keepRetrying = await retryHandler(retryContext);
                    if (!keepRetrying)
                    {
                        throw;
                    }

                    if (failureCount == int.MaxValue)
                    {
                        // Integer overflow safety check
                        throw;
                    }
                }
            }
        }

        class EventTaskCompletionSource<T> : TaskCompletionSource<T>, IEventSource
        {
            /// <inheritdoc/>
            public Type EventType => typeof(T);

            /// <inheritdoc/>
            public IEventSource? Next { get; set; }

            /// <inheritdoc/>
            void IEventSource.TrySetResult(object result) => this.TrySetResult((T)result);
        }

        interface IEventSource
        {
            /// <summary>
            /// The type of the event stored in the completion source.
            /// </summary>
            Type EventType { get; }

            /// <summary>
            /// The next task completion source in the stack.
            /// </summary>
            IEventSource? Next { get; set; }

            /// <summary>
            /// Tries to set the result on tcs.
            /// </summary>
            /// <param name="result">The result.</param>
            void TrySetResult(object result);
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

            public IEnumerable<(string eventName, TValue eventPayload)> TakeAll()
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
}
