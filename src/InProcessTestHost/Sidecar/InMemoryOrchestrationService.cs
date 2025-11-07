// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using DurableTask.Core;
using DurableTask.Core.Exceptions;
using DurableTask.Core.History;
using DurableTask.Core.Query;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.DurableTask.Testing.Sidecar;

/// <summary>
/// An in-memory implementation of orchestration service interfaces for testing purposes.
/// </summary>
/// <remarks>
/// This class provides a complete in-memory implementation of orchestration services,
/// allowing for testing of durable task orchestrations without external dependencies.
/// </remarks>
public class InMemoryOrchestrationService : IOrchestrationService, IOrchestrationServiceClient, IOrchestrationServiceQueryClient, IOrchestrationServicePurgeClient
{
    readonly InMemoryQueue activityQueue = new();
    readonly InMemoryInstanceStore instanceStore;
    readonly ILogger logger;

    /// <summary>
    /// Gets the number of task orchestration dispatchers.
    /// </summary>
    public int TaskOrchestrationDispatcherCount => 1;

    /// <summary>
    /// Gets the number of task activity dispatchers.
    /// </summary>
    public int TaskActivityDispatcherCount => 1;

    /// <summary>
    /// Gets the maximum number of concurrent task orchestration work items.
    /// </summary>
    public int MaxConcurrentTaskOrchestrationWorkItems => Environment.ProcessorCount;

    /// <summary>
    /// Gets the maximum number of concurrent task activity work items.
    /// </summary>
    public int MaxConcurrentTaskActivityWorkItems => Environment.ProcessorCount;

    /// <summary>
    /// Gets the behavior for events when continuing as new.
    /// </summary>
    public BehaviorOnContinueAsNew EventBehaviourForContinueAsNew => BehaviorOnContinueAsNew.Carryover;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryOrchestrationService"/> class.
    /// </summary>
    /// <param name="loggerFactory">Optional logger factory for creating loggers.</param>
    public InMemoryOrchestrationService(ILoggerFactory? loggerFactory = null)
    {
        this.logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger("Microsoft.DurableTask.Sidecar.InMemoryStorageProvider");
        this.instanceStore = new InMemoryInstanceStore(this.logger);
    }

    /// <summary>
    /// Abandons a task activity work item.
    /// </summary>
    /// <param name="workItem">The work item to abandon.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task AbandonTaskActivityWorkItemAsync(TaskActivityWorkItem workItem)
    {
        this.logger.LogWarning("Abandoning task activity work item {Id}", workItem.Id);
        this.activityQueue.Enqueue(workItem.TaskMessage);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Abandons a task orchestration work item.
    /// </summary>
    /// <param name="workItem">The work item to abandon.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task AbandonTaskOrchestrationWorkItemAsync(TaskOrchestrationWorkItem workItem)
    {
        this.instanceStore.AbandonInstance(workItem.NewMessages);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Completes a task activity work item.
    /// </summary>
    /// <param name="workItem">The work item to complete.</param>
    /// <param name="responseMessage">The response message.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task CompleteTaskActivityWorkItemAsync(TaskActivityWorkItem workItem, TaskMessage responseMessage)
    {
        this.instanceStore.AddMessage(responseMessage);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Completes a task orchestration work item.
    /// </summary>
    /// <param name="workItem">The work item to complete.</param>
    /// <param name="newOrchestrationRuntimeState">The new orchestration runtime state.</param>
    /// <param name="outboundMessages">The outbound messages.</param>
    /// <param name="orchestratorMessages">The orchestrator messages.</param>
    /// <param name="timerMessages">The timer messages.</param>
    /// <param name="continuedAsNewMessage">The continued as new message.</param>
    /// <param name="orchestrationState">The orchestration state.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task CompleteTaskOrchestrationWorkItemAsync(
        TaskOrchestrationWorkItem workItem,
        OrchestrationRuntimeState newOrchestrationRuntimeState,
        IList<TaskMessage> outboundMessages,
        IList<TaskMessage> orchestratorMessages,
        IList<TaskMessage> timerMessages,
        TaskMessage continuedAsNewMessage,
        OrchestrationState orchestrationState)
    {
        this.instanceStore.SaveState(
            runtimeState: newOrchestrationRuntimeState,
            statusRecord: orchestrationState,
            newMessages: orchestratorMessages.Union(timerMessages).Append(continuedAsNewMessage).Where(msg => msg != null));

        this.activityQueue.Enqueue(outboundMessages);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates the orchestration service.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task CreateAsync() => Task.CompletedTask;

    /// <summary>
    /// Creates the orchestration service.
    /// </summary>
    /// <param name="recreateInstanceStore">Whether to recreate the instance store.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task CreateAsync(bool recreateInstanceStore)
    {
        if (recreateInstanceStore)
        {
            this.instanceStore.Reset();
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates the orchestration service if it does not exist.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task CreateIfNotExistsAsync() => Task.CompletedTask;

    /// <summary>
    /// Creates a task orchestration.
    /// </summary>
    /// <param name="creationMessage">The creation message.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task CreateTaskOrchestrationAsync(TaskMessage creationMessage)
    {
        return this.CreateTaskOrchestrationAsync(
            creationMessage,
            new[] { OrchestrationStatus.Pending, OrchestrationStatus.Running });
    }

    /// <summary>
    /// Creates a task orchestration.
    /// </summary>
    /// <param name="creationMessage">The creation message.</param>
    /// <param name="dedupeStatuses">The deduplication statuses.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task CreateTaskOrchestrationAsync(TaskMessage creationMessage, OrchestrationStatus[]? dedupeStatuses)
    {
        // Lock the instance store to prevent multiple "create" threads from racing with each other.
        lock (this.instanceStore)
        {
            string instanceId = creationMessage.OrchestrationInstance.InstanceId;
            if (this.instanceStore.TryGetState(instanceId, out OrchestrationState? statusRecord) &&
                dedupeStatuses != null &&
                dedupeStatuses.Contains(statusRecord.OrchestrationStatus))
            {
                throw new OrchestrationAlreadyExistsException($"An orchestration with id '{instanceId}' already exists. It's in the {statusRecord.OrchestrationStatus} state.");
            }

            this.instanceStore.AddMessage(creationMessage);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Deletes the orchestration service.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task DeleteAsync() => this.DeleteAsync(true);

    /// <summary>
    /// Deletes the orchestration service.
    /// </summary>
    /// <param name="deleteInstanceStore">Whether to delete the instance store.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task DeleteAsync(bool deleteInstanceStore)
    {
        if (deleteInstanceStore)
        {
            this.instanceStore.Reset();
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Force terminates a task orchestration.
    /// </summary>
    /// <param name="instanceId">The instance ID.</param>
    /// <param name="reason">The termination reason.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task ForceTerminateTaskOrchestrationAsync(string instanceId, string reason)
    {
        var taskMessage = new TaskMessage
        {
            OrchestrationInstance = new OrchestrationInstance { InstanceId = instanceId },
            Event = new ExecutionTerminatedEvent(-1, reason),
        };

        return this.SendTaskOrchestrationMessageAsync(taskMessage);
    }

    /// <summary>
    /// Gets the delay in seconds after an on-fetch exception.
    /// </summary>
    /// <param name="exception">The exception that occurred.</param>
    /// <returns>The delay in seconds.</returns>
    public int GetDelayInSecondsAfterOnFetchException(Exception exception)
    {
        return exception is OperationCanceledException ? 0 : 1;
    }

    /// <summary>
    /// Gets the delay in seconds after an on-process exception.
    /// </summary>
    /// <param name="exception">The exception that occurred.</param>
    /// <returns>The delay in seconds.</returns>
    public int GetDelayInSecondsAfterOnProcessException(Exception exception)
    {
        return exception is OperationCanceledException ? 0 : 1;
    }

    /// <summary>
    /// Gets the orchestration history.
    /// </summary>
    /// <param name="instanceId">The instance ID.</param>
    /// <param name="executionId">The execution ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="NotImplementedException">This method is not implemented in the in-memory service.</exception>
    public Task<string> GetOrchestrationHistoryAsync(string instanceId, string executionId)
    {
        // Also not supported in the emulator
        throw new NotImplementedException();
    }

    /// <summary>
    /// Gets the orchestration state.
    /// </summary>
    /// <param name="instanceId">The instance ID.</param>
    /// <param name="allExecutions">Whether to get all executions.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task<IList<OrchestrationState>> GetOrchestrationStateAsync(string instanceId, bool allExecutions)
    {
        OrchestrationState state = await this.GetOrchestrationStateAsync(instanceId, executionId: null);
        return new[] { state };
    }

    /// <summary>
    /// Gets the orchestration state.
    /// </summary>
    /// <param name="instanceId">The instance ID.</param>
    /// <param name="executionId">The execution ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task<OrchestrationState> GetOrchestrationStateAsync(string instanceId, string? executionId)
    {
        if (this.instanceStore.TryGetState(instanceId, out OrchestrationState? statusRecord))
        {
            if (executionId == null || executionId == statusRecord.OrchestrationInstance.ExecutionId)
            {
                return Task.FromResult(statusRecord);
            }
        }

        return Task.FromResult<OrchestrationState>(null!);
    }

    /// <summary>
    /// Determines if the maximum message count is exceeded.
    /// </summary>
    /// <param name="currentMessageCount">The current message count.</param>
    /// <param name="runtimeState">The runtime state.</param>
    /// <returns>Always returns false for the in-memory service.</returns>
    public bool IsMaxMessageCountExceeded(int currentMessageCount, OrchestrationRuntimeState runtimeState) => false;

    /// <summary>
    /// Locks the next task activity work item.
    /// </summary>
    /// <param name="receiveTimeout">The receive timeout.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task<TaskActivityWorkItem> LockNextTaskActivityWorkItem(TimeSpan receiveTimeout, CancellationToken cancellationToken)
    {
        TaskMessage message = await this.activityQueue.DequeueAsync(cancellationToken);
        return new TaskActivityWorkItem
        {
            Id = message.SequenceNumber.ToString(CultureInfo.InvariantCulture), // CA1305: Use IFormatProvider
            LockedUntilUtc = DateTime.MaxValue,
            TaskMessage = message,
        };
    }

    /// <summary>
    /// Locks the next task orchestration work item.
    /// </summary>
    /// <param name="receiveTimeout">The receive timeout.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task<TaskOrchestrationWorkItem> LockNextTaskOrchestrationWorkItemAsync(TimeSpan receiveTimeout, CancellationToken cancellationToken)
    {
        var (instanceId, runtimeState, messages) = await this.instanceStore.GetNextReadyToRunInstanceAsync(cancellationToken);

        return new TaskOrchestrationWorkItem
        {
            InstanceId = instanceId,
            OrchestrationRuntimeState = runtimeState,
            NewMessages = messages,
            LockedUntilUtc = DateTime.MaxValue,
        };
    }

    /// <summary>
    /// Purges orchestration history.
    /// </summary>
    /// <param name="thresholdDateTimeUtc">The threshold date time in UTC.</param>
    /// <param name="timeRangeFilterType">The time range filter type.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="NotImplementedException">This method is not implemented in the in-memory service.</exception>
    public Task PurgeOrchestrationHistoryAsync(DateTime thresholdDateTimeUtc, OrchestrationStateTimeRangeFilterType timeRangeFilterType)
    {
        // Also not supported in the emulator
        throw new NotImplementedException();
    }

    /// <summary>
    /// Releases a task orchestration work item.
    /// </summary>
    /// <param name="workItem">The work item to release.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task ReleaseTaskOrchestrationWorkItemAsync(TaskOrchestrationWorkItem workItem)
    {
        this.instanceStore.ReleaseLock(workItem.InstanceId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Renews a task activity work item lock.
    /// </summary>
    /// <param name="workItem">The work item to renew.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task<TaskActivityWorkItem> RenewTaskActivityWorkItemLockAsync(TaskActivityWorkItem workItem)
    {
        return Task.FromResult(workItem); // PeekLock isn't supported
    }

    /// <summary>
    /// Renews a task orchestration work item lock.
    /// </summary>
    /// <param name="workItem">The work item to renew.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task RenewTaskOrchestrationWorkItemLockAsync(TaskOrchestrationWorkItem workItem)
    {
        return Task.CompletedTask; // PeekLock isn't supported
    }

    /// <summary>
    /// Sends a task orchestration message.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task SendTaskOrchestrationMessageAsync(TaskMessage message)
    {
        this.instanceStore.AddMessage(message);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sends a batch of task orchestration messages.
    /// </summary>
    /// <param name="messages">The messages to send.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task SendTaskOrchestrationMessageBatchAsync(params TaskMessage[] messages)
    {
        // NOTE: This is not transactionally consistent - some messages may get processed earlier than others.
        foreach (TaskMessage message in messages)
        {
            this.instanceStore.AddMessage(message);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Starts the orchestration service.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task StartAsync() => Task.CompletedTask;

    /// <summary>
    /// Stops the orchestration service.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task StopAsync() => Task.CompletedTask;

    /// <summary>
    /// Stops the orchestration service.
    /// </summary>
    /// <param name="isForced">Whether the stop is forced.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task StopAsync(bool isForced) => Task.CompletedTask;

    /// <summary>
    /// Waits for an orchestration to complete.
    /// </summary>
    /// <param name="instanceId">The instance ID.</param>
    /// <param name="executionId">The execution ID.</param>
    /// <param name="timeout">The timeout duration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task<OrchestrationState> WaitForOrchestrationAsync(string instanceId, string executionId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return await this.instanceStore.WaitForInstanceAsync(instanceId, cancellationToken);
        }
        else
        {
            using CancellationTokenSource timeoutCancellationSource = new(timeout);
            using CancellationTokenSource linkedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCancellationSource.Token);
            return await this.instanceStore.WaitForInstanceAsync(instanceId, linkedCancellationSource.Token);
        }
    }

    static bool TryGetScheduledTime(TaskMessage message, out TimeSpan delay)
    {
        DateTime scheduledTime = default;
        if (message.Event is ExecutionStartedEvent startEvent)
        {
            scheduledTime = startEvent.ScheduledStartTime ?? default;
        }
        else if (message.Event is TimerFiredEvent timerEvent)
        {
            scheduledTime = timerEvent.FireAt;
        }

        DateTime now = DateTime.UtcNow;
        if (scheduledTime > now)
        {
            delay = scheduledTime - now;
            return true;
        }
        else
        {
            delay = default;
            return false;
        }
    }

    /// <summary>
    /// Gets orchestrations with a query.
    /// </summary>
    /// <param name="query">The orchestration query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task<OrchestrationQueryResult> GetOrchestrationWithQueryAsync(OrchestrationQuery query, CancellationToken cancellationToken)
    {
        return Task.FromResult(this.instanceStore.GetOrchestrationWithQuery(query));
    }

    /// <summary>
    /// Purges the state of a specific orchestration instance.
    /// </summary>
    /// <param name="instanceId">The instance ID to purge.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task<PurgeResult> PurgeInstanceStateAsync(string instanceId)
    {
        return Task.FromResult(this.instanceStore.PurgeInstanceState(instanceId));
    }

    /// <summary>
    /// Purges orchestration instances based on a filter.
    /// </summary>
    /// <param name="purgeInstanceFilter">The purge instance filter.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task<PurgeResult> PurgeInstanceStateAsync(PurgeInstanceFilter purgeInstanceFilter)
    {
        return Task.FromResult(this.instanceStore.PurgeInstanceState(purgeInstanceFilter));
    }

    class InMemoryQueue
    {
        readonly Channel<TaskMessage> innerQueue = Channel.CreateUnbounded<TaskMessage>();

        public void Enqueue(TaskMessage taskMessage)
        {
            if (TryGetScheduledTime(taskMessage, out TimeSpan delay))
            {
                _ = Task.Delay(delay).ContinueWith(t => this.innerQueue.Writer.TryWrite(taskMessage));
            }
            else
            {
                this.innerQueue.Writer.TryWrite(taskMessage);
            }
        }

        public void Enqueue(IEnumerable<TaskMessage> messages)
        {
            foreach (TaskMessage msg in messages)
            {
                this.Enqueue(msg);
            }
        }

        public async Task<TaskMessage> DequeueAsync(CancellationToken cancellationToken)
        {
            return await this.innerQueue.Reader.ReadAsync(cancellationToken);
        }
    }

    class InMemoryInstanceStore
    {
        readonly ConcurrentDictionary<string, SerializedInstanceState> store = new(StringComparer.OrdinalIgnoreCase);
        readonly ConcurrentDictionary<string, TaskCompletionSource<OrchestrationState>> waiters = new(StringComparer.OrdinalIgnoreCase);
        readonly ReadyToRunQueue readyToRunQueue = new();

        readonly ILogger logger;

        public InMemoryInstanceStore(ILogger logger) => this.logger = logger;

        public void Reset()
        {
            this.store.Clear();
            this.waiters.Clear();
            this.readyToRunQueue.Reset();
        }

        public async Task<(string, OrchestrationRuntimeState, List<TaskMessage>)> GetNextReadyToRunInstanceAsync(CancellationToken cancellationToken)
        {
            SerializedInstanceState state = await this.readyToRunQueue.TakeNextAsync(cancellationToken);
            lock (state)
            {
                List<HistoryEvent> history = state.HistoryEventsJson.Select(e => e!.GetValue<HistoryEvent>()).ToList();
                OrchestrationRuntimeState runtimeState = new(history);

                List<TaskMessage> messages = state.MessagesJson.Select(node => node!.GetValue<TaskMessage>()).ToList();
                if (messages == null)
                {
                    throw new InvalidOperationException("Should never load state with zero messages.");
                }

                state.IsLoaded = true;

                // There is no "peek-lock" semantic. All dequeued messages are immediately deleted.
                state.MessagesJson.Clear();

                return (state.InstanceId, runtimeState, messages);
            }
        }

        public bool TryGetState(string instanceId, [NotNullWhen(true)] out OrchestrationState? statusRecord)
        {
            if (!this.store.TryGetValue(instanceId, out SerializedInstanceState? state))
            {
                statusRecord = null;
                return false;
            }

            statusRecord = state.StatusRecordJson?.GetValue<OrchestrationState>();
            return statusRecord != null;
        }

        public void SaveState(
            OrchestrationRuntimeState runtimeState,
            OrchestrationState statusRecord,
            IEnumerable<TaskMessage> newMessages)
        {
            static bool IsCompleted(OrchestrationRuntimeState runtimeState) =>
                runtimeState.OrchestrationStatus == OrchestrationStatus.Completed ||
                runtimeState.OrchestrationStatus == OrchestrationStatus.Failed ||
                runtimeState.OrchestrationStatus == OrchestrationStatus.Terminated ||
                runtimeState.OrchestrationStatus == OrchestrationStatus.Canceled;

            if (string.IsNullOrEmpty(runtimeState.OrchestrationInstance?.InstanceId))
            {
                throw new ArgumentException("The provided runtime state doesn't contain instance ID information.", nameof(runtimeState));
            }

            string instanceId = runtimeState.OrchestrationInstance.InstanceId;
            string executionId = runtimeState.OrchestrationInstance.ExecutionId;
            SerializedInstanceState state = this.store.GetOrAdd(
                instanceId,
                _ => new SerializedInstanceState(instanceId, executionId));
            lock (state)
            {
                if (state.ExecutionId == null)
                {
                    // This orchestration was started by a message without an execution ID.
                    state.ExecutionId = executionId;
                }
                else if (state.ExecutionId != executionId)
                {
                    // This is a new generation (ContinueAsNew). Erase the old history.
                    state.HistoryEventsJson.Clear();
                    state.ExecutionId = executionId;
                }

                foreach (TaskMessage msg in newMessages)
                {
                    this.AddMessage(msg);
                }

                // Append to the orchestration history
                foreach (HistoryEvent e in runtimeState.NewEvents)
                {
                    state.HistoryEventsJson.Add(e);
                }

                state.StatusRecordJson = JsonValue.Create(statusRecord);
                state.IsCompleted = IsCompleted(runtimeState);
            }

            // Notify any waiters of the orchestration completion
            if (IsCompleted(runtimeState) &&
                this.waiters.TryRemove(statusRecord.OrchestrationInstance.InstanceId, out TaskCompletionSource<OrchestrationState>? waiter))
            {
                waiter.TrySetResult(statusRecord);
            }
        }

        public void AddMessage(TaskMessage message)
        {
            string instanceId = message.OrchestrationInstance.InstanceId;
            string? executionId = message.OrchestrationInstance.ExecutionId;

            SerializedInstanceState state = this.store.GetOrAdd(instanceId, id => new SerializedInstanceState(id, executionId));
            lock (state)
            {
                bool isRestart = state.ExecutionId != null && state.ExecutionId != executionId;
                
                if (message.Event is ExecutionStartedEvent startEvent)
                {
                    // For restart scenarios, clear the history and reset the state
                    if (isRestart && state.IsCompleted)
                    {
                        state.ExecutionId = executionId;
                        state.IsLoaded = false;
                    }
                    
                    OrchestrationState newStatusRecord = new()
                    {
                        OrchestrationInstance = startEvent.OrchestrationInstance,
                        CreatedTime = DateTime.UtcNow,
                        LastUpdatedTime = DateTime.UtcNow,
                        OrchestrationStatus = OrchestrationStatus.Pending,
                        Version = startEvent.Version,
                        Name = startEvent.Name,
                        Input = startEvent.Input,
                        ScheduledStartTime = startEvent.ScheduledStartTime,
                        Tags = startEvent.Tags,
                    };

                    state.StatusRecordJson = JsonValue.Create(newStatusRecord);
                    state.HistoryEventsJson.Clear();
                    state.IsCompleted = false;
                }
                else if (state.IsCompleted)
                {
                    // Drop the message since we're completed
                    // GOOD: The user-provided the instanceId
                    // logger.LogWarning(
                    //     "Dropped {eventType} message for instance '{instanceId}' because the orchestration has already completed.",
                    //     message.Event.EventType,
                    //     instanceId);
                    return;
                }

                if (TryGetScheduledTime(message, out TimeSpan delay))
                {
                    // Not ready for this message yet - delay the enqueue
                    _ = Task.Delay(delay).ContinueWith(t => this.AddMessage(message));
                    return;
                }

                state.MessagesJson.Add(message);

                if (!state.IsLoaded)
                {
                    // The orchestration isn't running, so schedule it to run now.
                    // If it is running, it will be scheduled again automatically when it's released.
                    this.readyToRunQueue.Schedule(state);
                }
            }
        }

        public void AbandonInstance(IEnumerable<TaskMessage> messagesToReturn)
        {
            foreach (var message in messagesToReturn)
            {
                this.AddMessage(message);
            }
        }

        public void ReleaseLock(string instanceId)
        {
            if (!this.store.TryGetValue(instanceId, out SerializedInstanceState? state) || !state.IsLoaded)
            {
                throw new InvalidOperationException($"Instance {instanceId} is not in the store or is not loaded!");
            }

            lock (state)
            {
                state.IsLoaded = false;
                if (state.MessagesJson.Count > 0)
                {
                    // More messages came in while we were running. Or, messages were abandoned.
                    // Put this back into the read-to-run queue!
                    this.readyToRunQueue.Schedule(state);
                }
            }
        }

        public Task<OrchestrationState> WaitForInstanceAsync(string instanceId, CancellationToken cancellationToken)
        {
            if (this.store.TryGetValue(instanceId, out SerializedInstanceState? state))
            {
                lock (state)
                {
                    OrchestrationState? statusRecord = state.StatusRecordJson?.GetValue<OrchestrationState>();
                    if (statusRecord != null)
                    {
                        if (statusRecord.OrchestrationStatus == OrchestrationStatus.Completed ||
                            statusRecord.OrchestrationStatus == OrchestrationStatus.Failed ||
                            statusRecord.OrchestrationStatus == OrchestrationStatus.Terminated)
                        {
                            // orchestration has already completed
                            return Task.FromResult(statusRecord);
                        }
                    }
                }
            }

            // Caller will be notified when the instance completes.
            // The ContinueWith is just to enable cancellation: https://stackoverflow.com/a/25652873/2069
            var tcs = this.waiters.GetOrAdd(instanceId, _ => new TaskCompletionSource<OrchestrationState>());
            return tcs.Task.ContinueWith(t => t.GetAwaiter().GetResult(), cancellationToken);
        }

        public OrchestrationQueryResult GetOrchestrationWithQuery(OrchestrationQuery query)
        {
            int startIndex = 0;
            int counter = 0;
            string? continuationToken = query.ContinuationToken;
            if (continuationToken != null)
            {
                if (!Int32.TryParse(continuationToken, out startIndex))
                {
                    throw new InvalidOperationException($"{continuationToken} cannot be parsed to Int32");
                }
            }

            counter = startIndex;

            List<OrchestrationState> results = this.store
                .Skip(startIndex)
                .Where(item =>
                {
                    counter++;
                    OrchestrationState? statusRecord = item.Value.StatusRecordJson?.GetValue<OrchestrationState>();
                    if (statusRecord == null)
                    {
                        return false;
                    }

                    if (query.CreatedTimeFrom != null && query.CreatedTimeFrom > statusRecord.CreatedTime)
                    {
                        return false;
                    }

                    if (query.CreatedTimeTo != null && query.CreatedTimeTo < statusRecord.CreatedTime)
                    {
                        return false;
                    }

                    if (query.RuntimeStatus != null && query.RuntimeStatus.Count != 0 && !query.RuntimeStatus.Contains(statusRecord.OrchestrationStatus))
                    {
                        return false;
                    }

                    // CA1310: Use StringComparison for culture-sensitive operations
                    if (query.InstanceIdPrefix != null && !statusRecord.OrchestrationInstance.InstanceId.StartsWith(query.InstanceIdPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    return true;
                })
                .Take(query.PageSize)
                .Select(item => item.Value.StatusRecordJson!.GetValue<OrchestrationState>())
                .ToList();

            string? token = null;
            if (results.Count == query.PageSize)
            {
                // CA1305: Use IFormatProvider
                token = counter.ToString(CultureInfo.InvariantCulture);
            }

            return new OrchestrationQueryResult(results, token);
        }

        public PurgeResult PurgeInstanceState(string instanceId)
        {
            if (instanceId != null && this.store.TryGetValue(instanceId, out SerializedInstanceState? state) && state.IsCompleted)
            {
                this.store.TryRemove(instanceId, out SerializedInstanceState? removedState);
                if (removedState != null)
                {
                    return new PurgeResult(1);
                }
            }
            return new PurgeResult(0);
        }

        public PurgeResult PurgeInstanceState(PurgeInstanceFilter purgeInstanceFilter)
        {
            int counter = 0;

            List<string> filteredInstanceIds = this.store
                .Where(item =>
                {
                    OrchestrationState? statusRecord = item.Value.StatusRecordJson?.GetValue<OrchestrationState>();
                    if (statusRecord == null) return false;
                    if (purgeInstanceFilter.CreatedTimeFrom > statusRecord.CreatedTime) return false;
                    if (purgeInstanceFilter.CreatedTimeTo != null && purgeInstanceFilter.CreatedTimeTo < statusRecord.CreatedTime) return false;
                    if (purgeInstanceFilter.RuntimeStatus != null && purgeInstanceFilter.RuntimeStatus.Any() && !purgeInstanceFilter.RuntimeStatus.Contains(statusRecord.OrchestrationStatus)) return false;
                    return true;
                })
                .Select(item => item.Key)
                .ToList();

            foreach (string instanceId in filteredInstanceIds)
            {
                this.store.TryRemove(instanceId, out SerializedInstanceState? removedState);
                if (removedState != null)
                {
                    counter++;
                }
            }

            return new PurgeResult(counter);
        }

        class ReadyToRunQueue
        {
            readonly Channel<SerializedInstanceState> readyToRunQueue = Channel.CreateUnbounded<SerializedInstanceState>();
            readonly Dictionary<string, object> readyInstances = new(StringComparer.OrdinalIgnoreCase);

            public void Reset()
            {
                this.readyInstances.Clear();
            }

            public async ValueTask<SerializedInstanceState> TakeNextAsync(CancellationToken ct)
            {
                while (true)
                {
                    SerializedInstanceState state = await this.readyToRunQueue.Reader.ReadAsync(ct);
                    lock (state)
                    {
                        if (this.readyInstances.Remove(state.InstanceId))
                        {
                            if (state.IsLoaded)
                            {
                                throw new InvalidOperationException("Should never load state that is already loaded.");
                            }

                            state.IsLoaded = true;
                            return state;
                        }
                    }
                }
            }

            public void Schedule(SerializedInstanceState state)
            {
                // TODO: There is a race condition here. If another thread is calling TakeNextAsync
                //       and removed the queue item before updating the dictionary, then we'll fail
                //       to update the readyToRunQueue and the orchestration will get stuck.
                if (this.readyInstances.TryAdd(state.InstanceId, state))
                {
                    if (!this.readyToRunQueue.Writer.TryWrite(state)) 
                    {
                        throw new InvalidOperationException($"unable to write to queue for {state.InstanceId}");
                    }
                }
            }
        }

        class SerializedInstanceState
        {
            public SerializedInstanceState(string instanceId, string? executionId)
            {
                this.InstanceId = instanceId;
                this.ExecutionId = executionId;
            }

            public string InstanceId { get; }
            public string? ExecutionId { get; internal set; }
            public JsonValue? StatusRecordJson { get; set; }
            public JsonArray HistoryEventsJson { get; } = new JsonArray();
            public JsonArray MessagesJson { get; } = new JsonArray();

            internal bool IsLoaded { get; set; }
            internal bool IsCompleted { get; set; }
        }
    }
}

