// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DurableTask.Core;
using DurableTask.Core.Entities.OperationFormat;
using DurableTask.Core.Serializing.Internal;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker.Shims;

/// <summary>
/// A wrapper to go from <see cref="OrchestrationContext" /> to <see cref="TaskOrchestrationContext "/>.
/// </summary>
sealed partial class TaskOrchestrationContextWrapper : TaskOrchestrationContext
{
    readonly Dictionary<string, Queue<IEventSource>> externalEventSources = new(StringComparer.OrdinalIgnoreCase);
    readonly NamedQueue<string> externalEventBuffer = new();
    readonly OrchestrationContext innerContext;
    readonly OrchestrationInvocationContext invocationContext;
    readonly ILogger logger;
    readonly object? deserializedInput;

    int newGuidCounter;
    object? customStatus;
    TaskOrchestrationEntityContext? entityFeature;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskOrchestrationContextWrapper"/> class.
    /// </summary>
    /// <param name="innerContext">The inner orchestration context.</param>
    /// <param name="invocationContext">The invocation context.</param>
    /// <param name="deserializedInput">The deserialized input.</param>
    public TaskOrchestrationContextWrapper(
        OrchestrationContext innerContext,
        OrchestrationInvocationContext invocationContext,
        object? deserializedInput)
    {
        this.innerContext = Check.NotNull(innerContext);
        this.invocationContext = Check.NotNull(invocationContext);
        this.logger = this.CreateReplaySafeLogger("Microsoft.DurableTask");
        this.deserializedInput = deserializedInput;
    }

    /// <inheritdoc/>
    public override TaskName Name => this.invocationContext.Name;

    /// <inheritdoc/>
    public override string InstanceId => this.innerContext.OrchestrationInstance.InstanceId;

    /// <inheritdoc/>
    public override string InstanceVersion => this.innerContext.OrchestrationInstance.InstanceVersion;

    /// <inheritdoc/>
    public override ParentOrchestrationInstance? Parent => this.invocationContext.Parent;

    /// <inheritdoc/>
    public override bool IsReplaying => this.innerContext.IsReplaying;

    /// <inheritdoc/>
    public override DateTime CurrentUtcDateTime => this.innerContext.CurrentUtcDateTime;

    /// <inheritdoc/>
    public override TaskOrchestrationEntityFeature Entities
    {
        get
        {
            if (this.entityFeature == null)
            {
                if (this.invocationContext.Options.EnableEntitySupport)
                {
                    this.entityFeature = new TaskOrchestrationEntityContext(this);
                }
                else
                {
                    throw new NotSupportedException($"Durable entities are disabled because {nameof(DurableTaskWorkerOptions)}.{nameof(DurableTaskWorkerOptions.EnableEntitySupport)}=false");
                }
            }

            return this.entityFeature;
        }
    }

    /// <summary>
    /// Gets the DataConverter to use for inputs, outputs, and entity states.
    /// </summary>
    internal DataConverter DataConverter => this.invocationContext.Options.DataConverter;

    /// <inheritdoc/>
    protected override ILoggerFactory LoggerFactory => this.invocationContext.LoggerFactory;

    /// <inheritdoc/>
    public override T GetInput<T>() => (T)this.deserializedInput!;

    /// <inheritdoc/>
    public override async Task<T> CallActivityAsync<T>(
        TaskName name,
        object? input = null,
        TaskOptions? options = null)
    {
        // Since the input parameter takes any object, it's possible that callers may accidentally provide a
        // TaskOptions parameter here when the actually meant to provide TaskOptions for the optional options
        // parameter.
        if (input is TaskOptions && options == null)
        {
            throw new ArgumentException(
                $"A {nameof(TaskOptions)} value was provided for the activity input but no value was provided for"
                + $" {nameof(options)}. Did you actually mean to provide a {nameof(TaskOptions)} value for the"
                + $" {nameof(options)} parameter?",
                nameof(input));
        }

        try
        {
            // TODO: Cancellation (https://github.com/microsoft/durabletask-dotnet/issues/7)
            if (options?.Retry?.Policy is RetryPolicy policy)
            {
                return await this.innerContext.ScheduleWithRetry<T>(
                    name.Name,
                    name.Version,
                    policy.ToDurableTaskCoreRetryOptions(),
                    input);
            }
            else if (options?.Retry?.Handler is AsyncRetryHandler handler)
            {
                return await this.InvokeWithCustomRetryHandler(
                    () => this.innerContext.ScheduleTask<T>(name.Name, name.Version, input),
                    name.Name,
                    handler,
                    default);
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

    /// <inheritdoc/>
    public override async Task<TResult> CallSubOrchestratorAsync<TResult>(
        TaskName orchestratorName,
        object? input = null,
        TaskOptions? options = null)
    {
        // TODO: Check to see if this orchestrator is defined
        static string? GetInstanceId(TaskOptions? options)
            => options is SubOrchestrationOptions derived ? derived.InstanceId : null;
        string instanceId = GetInstanceId(options) ?? this.NewGuid().ToString("N");

        Check.NotEntity(this.invocationContext.Options.EnableEntitySupport, instanceId);

        // if this orchestration uses entities, first validate that the suborchsestration call is allowed in the current context
        if (this.entityFeature != null && !this.entityFeature.EntityContext.ValidateSuborchestrationTransition(out string? errorMsg))
        {
            throw new InvalidOperationException(errorMsg);
        }

        try
        {
            if (options?.Retry?.Policy is RetryPolicy policy)
            {
                return await this.innerContext.CreateSubOrchestrationInstanceWithRetry<TResult>(
                    orchestratorName.Name,
                    orchestratorName.Version,
                    instanceId,
                    policy.ToDurableTaskCoreRetryOptions(),
                    input);
            }
            else if (options?.Retry?.Handler is AsyncRetryHandler handler)
            {
                return await this.InvokeWithCustomRetryHandler(
                    () => this.innerContext.CreateSubOrchestrationInstance<TResult>(
                        orchestratorName.Name,
                        orchestratorName.Version,
                        instanceId,
                        input),
                    orchestratorName.Name,
                    handler,
                    default);
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

    /// <inheritdoc/>
    public override async Task CreateTimer(DateTime fireAt, CancellationToken cancellationToken)
    {
        // Make sure we're always operating in UTC
        DateTime finalFireAtUtc = fireAt.ToUniversalTime();

        // Longer timers are broken down into smaller timers. For example, if fireAt is 7 days from now
        // and the max interval is 3 days, there will be two 3-day timers and a single one-day timer.
        // This is primarily to support backends that don't support infinite timers, like Azure Storage.
        TimeSpan maximumTimerInterval = this.invocationContext.Options.MaximumTimerInterval;
        TimeSpan remainingTime = finalFireAtUtc.Subtract(this.CurrentUtcDateTime);
        while (remainingTime > maximumTimerInterval && !cancellationToken.IsCancellationRequested)
        {
            DateTime nextFireAt = this.CurrentUtcDateTime.Add(maximumTimerInterval);
            await this.innerContext.CreateTimer<object>(nextFireAt, state: null!, cancellationToken);
            remainingTime = finalFireAtUtc.Subtract(this.CurrentUtcDateTime);
        }

        await this.innerContext.CreateTimer<object>(finalFireAtUtc, state: null!, cancellationToken);
    }

    /// <inheritdoc/>
    public override Task<T> WaitForExternalEvent<T>(string eventName, CancellationToken cancellationToken = default)
    {
        // Return immediately if this external event has already arrived.
        if (this.externalEventBuffer.TryTake(eventName, out string? bufferedEventPayload))
        {
            return Task.FromResult(this.DataConverter.Deserialize<T>(bufferedEventPayload));
        }

        // Create a task completion source that will be set when the external event arrives.
        EventTaskCompletionSource<T> eventSource = new();
        if (this.externalEventSources.TryGetValue(eventName, out Queue<IEventSource>? existing))
        {
            if (existing.Count > 0 && existing.Peek().EventType != typeof(T))
            {
                throw new ArgumentException("Events with the same name must have the same type argument. Expected"
                    + $" {existing.Peek().GetType().FullName} but was requested {typeof(T).FullName}.");
            }

            existing.Enqueue(eventSource);
        }
        else
        {
            Queue<IEventSource> eventSourceQueue = new();
            eventSourceQueue.Enqueue(eventSource);
            this.externalEventSources.Add(eventName, eventSourceQueue);
        }

        // TODO: this needs to be tracked and disposed appropriately.
        cancellationToken.Register(() => eventSource.TrySetCanceled(cancellationToken));
        return eventSource.Task;
    }

    /// <inheritdoc/>
    public override void SendEvent(string instanceId, string eventName, object eventData)
    {
        Check.NotEntity(this.invocationContext.Options.EnableEntitySupport, instanceId);

        this.innerContext.SendEvent(new OrchestrationInstance { InstanceId = instanceId }, eventName, eventData);
    }

    /// <inheritdoc/>
    public override void SetCustomStatus(object? customStatus)
    {
        this.customStatus = customStatus;
    }

    /// <inheritdoc/>
    public override void ContinueAsNew(object? newInput = null, bool preserveUnprocessedEvents = true)
    {
        this.innerContext.ContinueAsNew(newInput);

        if (preserveUnprocessedEvents)
        {
            // Send all the buffered external events to ourself.
            OrchestrationInstance instance = new() { InstanceId = this.InstanceId };
            foreach ((string eventName, string eventPayload) in this.externalEventBuffer.TakeAll())
            {
#pragma warning disable CS0618 // Type or member is obsolete -- 'internal' usage.
                this.innerContext.SendEvent(instance, eventName, new RawInput(eventPayload));
#pragma warning restore CS0618 // Type or member is obsolete
            }
        }
    }

    /// <inheritdoc/>
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
            (byteArray[right], byteArray[left]) = (byteArray[left], byteArray[right]);
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
            this.newGuidCounter.ToString(CultureInfo.InvariantCulture));

        this.newGuidCounter++;

        byte[] nameByteArray = Encoding.UTF8.GetBytes(guidNameValue);
        byte[] namespaceValueByteArray = namespaceValueGuid.ToByteArray();
        SwapByteArrayValues(namespaceValueByteArray);

        byte[] hashByteArray;
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms -- not for cryptography
        using (HashAlgorithm hashAlgorithm = SHA1.Create())
        {
            hashAlgorithm.TransformBlock(namespaceValueByteArray, 0, namespaceValueByteArray.Length, null, 0);
            hashAlgorithm.TransformFinalBlock(nameByteArray, 0, nameByteArray.Length);
            hashByteArray = hashAlgorithm.Hash;
        }
#pragma warning restore CA5350 // Do Not Use Weak Cryptographic Algorithms -- not for cryptography

        byte[] newGuidByteArray = new byte[16];
        Array.Copy(hashByteArray, 0, newGuidByteArray, 0, 16);

        int versionValue = DeterministicGuidVersion;
        newGuidByteArray[6] = (byte)((newGuidByteArray[6] & 0x0F) | (versionValue << 4));
        newGuidByteArray[8] = (byte)((newGuidByteArray[8] & 0x3F) | 0x80);

        SwapByteArrayValues(newGuidByteArray);

        return new Guid(newGuidByteArray);
    }

    /// <summary>
    /// exits the critical section, if currently within a critical section. Otherwise, this has no effect.
    /// </summary>
    internal void ExitCriticalSectionIfNeeded()
    {
        this.entityFeature?.ExitCriticalSection();
    }

    /// <summary>
    /// Completes the external event by name, allowing the orchestration to continue if it is waiting on this event.
    /// </summary>
    /// <param name="eventName">The name of the event to complete.</param>
    /// <param name="rawEventPayload">The serialized event payload.</param>
    internal void CompleteExternalEvent(string eventName, string rawEventPayload)
    {
        if (this.externalEventSources.TryGetValue(eventName, out Queue<IEventSource>? waiters))
        {
            object? value;

            IEventSource waiter = waiters.Dequeue();
            if (waiter.EventType == typeof(OperationResult))
            {
                // use the framework-defined deserialization for entity responses, not the application-defined data converter,
                // because we are just unwrapping the entity response without yet deserializing any application-defined data.
                value = this.entityFeature!.EntityContext.DeserializeEntityResponseEvent(rawEventPayload);
            }
            else
            {
                value = this.DataConverter.Deserialize(rawEventPayload, waiter.EventType);
            }

            // Events are completed in FIFO order. Remove the key if the last event was delivered.
            if (waiters.Count == 0)
            {
                this.externalEventSources.Remove(eventName);
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

    /// <summary>
    /// Gets the serialized custom status.
    /// </summary>
    /// <returns>The custom status serialized to a string, or <c>null</c> if there is not custom status.</returns>
    internal string? GetSerializedCustomStatus()
    {
        return this.DataConverter.Serialize(this.customStatus);
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
                // Some failures are not retryable, like failures for missing activities or sub-orchestrations
                if (e.FailureDetails?.IsNonRetriable == true)
                {
                    throw;
                }

                failureCount++;

                this.logger.RetryingTask(
                    this.InstanceId,
                    taskName,
                    attempt: failureCount);

                RetryContext retryContext = new(
                    this,
                    failureCount,
                    TaskFailureDetails.FromException(e),
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
}
