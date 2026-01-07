// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
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
    // We use a stack (a custom implementation using a single-linked list) to make it easier for users
    // to abandon external events that they no longer care about. The common case is a Task.WhenAny in a loop.
    // Events are assigned to the most recent (top of stack) waiter, which naturally avoids issues with cancelled waiters.
    readonly Dictionary<string, IEventSource> externalEventSources = new(StringComparer.OrdinalIgnoreCase);
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
        : this(innerContext, invocationContext, deserializedInput, new Dictionary<string, object?>())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskOrchestrationContextWrapper"/> class.
    /// </summary>
    /// <param name="innerContext">The inner orchestration context.</param>
    /// <param name="invocationContext">The invocation context.</param>
    /// <param name="deserializedInput">The deserialized input.</param>
    /// <param name="properties">The configuration for context.</param>
    public TaskOrchestrationContextWrapper(
        OrchestrationContext innerContext,
        OrchestrationInvocationContext invocationContext,
        object? deserializedInput,
        IReadOnlyDictionary<string, object?> properties)
    {
        this.innerContext = Check.NotNull(innerContext);
        this.invocationContext = Check.NotNull(invocationContext);
        this.Properties = Check.NotNull(properties);

        this.logger = this.CreateReplaySafeLogger("Microsoft.DurableTask");
        this.deserializedInput = deserializedInput;
    }

    /// <inheritdoc/>
    public override TaskName Name => this.invocationContext.Name;

    /// <inheritdoc/>
    public override string InstanceId => this.innerContext.OrchestrationInstance.InstanceId;

    /// <inheritdoc/>
    public override ParentOrchestrationInstance? Parent => this.invocationContext.Parent;

    /// <inheritdoc/>
    public override bool IsReplaying => this.innerContext.IsReplaying;

    /// <inheritdoc/>
    public override DateTime CurrentUtcDateTime => this.innerContext.CurrentUtcDateTime;

    /// <summary>
    /// Gets the configuration settings for the orchestration.
    /// </summary>
    public override IReadOnlyDictionary<string, object?> Properties { get; }

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

    /// <inheritdoc/>
    public override string Version => this.innerContext.Version;

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
            IDictionary<string, string> tags = ImmutableDictionary<string, string>.Empty;
            CancellationToken cancellationToken = default;
            if (options is TaskOptions callActivityOptions)
            {
                if (callActivityOptions.Tags is not null)
                {
                    tags = callActivityOptions.Tags;
                }

                cancellationToken = callActivityOptions.CancellationToken;
            }

            // If cancellation was requested before starting, return a cancelled task immediately
            if (cancellationToken.IsCancellationRequested)
            {
                throw new TaskCanceledException("The task was cancelled before it could be scheduled.");
            }

#pragma warning disable 0618
            if (options?.Retry?.Policy is RetryPolicy policy)
            {
                Task<T> activityTask = this.innerContext.ScheduleTask<T>(
                    name.Name,
                    this.innerContext.Version,
                    options: ScheduleTaskOptions.CreateBuilder()
                        .WithRetryOptions(policy.ToDurableTaskCoreRetryOptions())
                        .WithTags(tags)
                        .Build(),
                    parameters: input);

                return await this.WaitForTaskWithCancellation(activityTask, cancellationToken);
            }
            else if (options?.Retry?.Handler is AsyncRetryHandler handler)
            {
                return await this.InvokeWithCustomRetryHandler(
                    () => this.innerContext.ScheduleTask<T>(
                        name.Name,
                        this.innerContext.Version,
                        options: ScheduleTaskOptions.CreateBuilder()
                            .WithTags(tags)
                            .Build(),
                        parameters: input),
                    name.Name,
                    handler,
                    cancellationToken);
            }
            else
            {
                Task<T> activityTask = this.innerContext.ScheduleTask<T>(
                    name.Name,
                    this.innerContext.Version,
                    options: ScheduleTaskOptions.CreateBuilder()
                        .WithTags(tags)
                        .Build(),
                    parameters: input);

                return await this.WaitForTaskWithCancellation(activityTask, cancellationToken);
            }
        }
        catch (global::DurableTask.Core.Exceptions.TaskFailedException e)
        {
            // Hide the core DTFx types and instead use our own
            throw new TaskFailedException(name, e.ScheduleId, e);
        }
#pragma warning restore 0618
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
        string defaultVersion = this.GetDefaultVersion();
        string version = options is SubOrchestrationOptions { Version: { } v } ? v.Version : defaultVersion;
        Check.NotEntity(this.invocationContext.Options.EnableEntitySupport, instanceId);

        // if this orchestration uses entities, first validate that the suborchestration call is allowed in the current context
        if (this.entityFeature != null && !this.entityFeature.EntityContext.ValidateSuborchestrationTransition(out string? errorMsg))
        {
            throw new InvalidOperationException(errorMsg);
        }

        CancellationToken cancellationToken = options?.CancellationToken ?? default;

        // If cancellation was requested before starting, return a cancelled task immediately
        if (cancellationToken.IsCancellationRequested)
        {
            throw new TaskCanceledException("The sub-orchestrator was cancelled before it could be scheduled.");
        }

        try
        {
            if (options?.Retry?.Policy is RetryPolicy policy)
            {
                Task<TResult> subOrchestratorTask = this.innerContext.CreateSubOrchestrationInstanceWithRetry<TResult>(
                    orchestratorName.Name,
                    version,
                    instanceId,
                    policy.ToDurableTaskCoreRetryOptions(),
                    input,
                    options.Tags);

                return await this.WaitForTaskWithCancellation(subOrchestratorTask, cancellationToken);
            }
            else if (options?.Retry?.Handler is AsyncRetryHandler handler)
            {
                return await this.InvokeWithCustomRetryHandler(
                    () => this.innerContext.CreateSubOrchestrationInstance<TResult>(
                        orchestratorName.Name,
                        version,
                        instanceId,
                        input,
                        options?.Tags),
                    orchestratorName.Name,
                    handler,
                    cancellationToken);
            }
            else
            {
                Task<TResult> subOrchestratorTask = this.innerContext.CreateSubOrchestrationInstance<TResult>(
                    orchestratorName.Name,
                    version,
                    instanceId,
                    input,
                    options?.Tags);

                return await this.WaitForTaskWithCancellation(subOrchestratorTask, cancellationToken);
            }
        }
        catch (global::DurableTask.Core.Exceptions.SubOrchestrationFailedException e)
        {
            // Hide the core DTFx types and instead use our own
            throw new TaskFailedException(
                orchestratorName,
                e.ScheduleId,
                TaskFailureDetails.FromCoreFailureDetails(e.FailureDetails!));
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
        // Create a task completion source that will be set when the external event arrives.
        EventTaskCompletionSource<T> eventSource = new();

        // Set up the stack for listening to external events (LIFO - Last In First Out)
        // New waiters are added to the top of the stack, so they get events first.
        // This makes it easier for users to abandon external events they no longer care about.
        // The common case is a Task.WhenAny in a loop.
        if (this.externalEventSources.TryGetValue(eventName, out IEventSource? existing))
        {
            if (existing.EventType != typeof(T))
            {
                throw new ArgumentException("Events with the same name must have the same type argument. Expected"
                    + $" {existing.EventType.FullName} but was requested {typeof(T).FullName}.");
            }

            // Add new waiter to the top of the stack
            eventSource.Next = existing;
        }

        // New waiter becomes the top of the stack
        this.externalEventSources[eventName] = eventSource;

        // Check the buffer to see if any events came in before the orchestrator was listening
        if (this.externalEventBuffer.TryTake(eventName, out string? bufferedEvent))
        {
            // We can complete the event right away, since we already have an event's input
            this.CompleteExternalEvent(eventName, bufferedEvent);
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
        using (HashAlgorithm hashAlgorithm = SHA1.Create()) /* CodeQL [SM02196] Suppressed: SHA1 is not used for cryptographic purposes here. The information being hashed is not sensitive,
                                                               and the goal is to generate a deterministic Guid. We cannot update to SHA2-based algorithms without breaking
                                                               customers' inflight orchestrations. */
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
        if (this.externalEventSources.TryGetValue(eventName, out IEventSource? waiter))
        {
            // Get the waiter at the top of the stack (most recent waiter)
            // If we're going to raise an event we should remove it from the pending collection
            // because otherwise WaitForExternalEvent() will always find one with this key and run infinitely.
            IEventSource? next = waiter.Next;
            if (next == null)
            {
                this.externalEventSources.Remove(eventName);
            }
            else
            {
                // Next waiter becomes the new top of the stack
                this.externalEventSources[eventName] = next;
            }

            object? value;
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

    async Task<T> WaitForTaskWithCancellation<T>(Task<T> task, CancellationToken cancellationToken)
    {
        // If no cancellation token provided or it can't be cancelled, just await the task
        if (!cancellationToken.CanBeCanceled)
        {
            return await task;
        }

        // If the task is already completed, return immediately without setting up cancellation infrastructure
        if (task.IsCompleted)
        {
            return await task;
        }

        // Create a cancellation task that completes when the token is cancelled
        TaskCompletionSource<T> cancellationTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            cancellationTcs.TrySetCanceled(cancellationToken);
        });

        // Wait for either the task to complete or cancellation
        Task<T> completedTask = await Task.WhenAny(task, cancellationTcs.Task);

        // If cancellation won, throw TaskCanceledException
        if (completedTask == cancellationTcs.Task)
        {
            throw new TaskCanceledException("The task was cancelled.");
        }

        // Otherwise return the result of the completed task
        return await task;
    }

    // The default version can come from two different places depending on the context of the invocation.
    string GetDefaultVersion()
    {
        // Preferred choice.
        if (this.invocationContext.Options.Versioning?.DefaultVersion is { } v)
        {
            return v;
        }

        // Secondary choice.
        if (this.Properties.TryGetValue("defaultVersion", out object? propVersion) && propVersion is string v2)
        {
            return v2;
        }

        return string.Empty;
    }
}
