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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DurableTask.Core;
using DurableTask.Core.Command;
using DurableTask.Core.History;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static DurableTask.Protobuf.TaskHubSidecarService;
using P = DurableTask.Protobuf;

namespace DurableTask.Grpc;

public class TaskHubGrpcWorker : IHostedService, IAsyncDisposable
{
    readonly GrpcChannel sidecarGrpcChannel;
    readonly TaskHubSidecarServiceClient sidecarClient;
    readonly IDataConverter dataConverter;
    readonly ILogger logger;

    readonly ImmutableDictionary<TaskName, Func<TaskOrchestration>> orchestrators;
    readonly ImmutableDictionary<TaskName, Func<TaskActivity>> activities;

    CancellationTokenSource? shutdownTcs;
    Task? listenLoop;

    TaskHubGrpcWorker(Builder builder)
    {
        this.sidecarGrpcChannel = GrpcChannel.ForAddress(builder.address);
        this.sidecarClient = new TaskHubSidecarServiceClient(this.sidecarGrpcChannel);
        this.dataConverter = builder.dataConverter;
        this.logger = SdkUtils.GetLogger(builder.loggerFactory);

        this.orchestrators = builder.orchestratorsBuilder.ToImmutable();
        this.activities = builder.activitiesBuilder.ToImmutable();
    }

    /// <summary>
    /// Establishes a gRPC connection to the sidecar and starts processing work-items in the background.
    /// </summary>
    /// <remarks>
    /// This method retries continuously to establish a connection to the sidecar. If a connection fails,
    /// a warning log message will be written and a new connection attempt will be made. This process
    /// continues until either a connection succeeds or the caller cancels the start operation.
    /// </remarks>
    /// <param name="startupCancelToken">
    /// A cancellation token that can be used to cancel the sidecar connection attempt if it takes too long.
    /// </param>
    /// <returns>
    /// Returns a task that completes when the sidecar connection has been established and the background processing started.
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown if this worker is already started.</exception>
    public async Task StartAsync(CancellationToken startupCancelToken)
    {
        if (this.listenLoop?.IsCompleted == false)
        {
            throw new InvalidOperationException($"This {nameof(TaskHubGrpcWorker)} is already started.");
        }

        this.logger.StartingTaskHubWorker(this.sidecarGrpcChannel.Target);

        // Keep trying to connect until the caller cancels
        while (true)
        {
            try
            {
                AsyncServerStreamingCall<P.WorkItem>? workItemStream = this.Connect(startupCancelToken);

                this.shutdownTcs?.Dispose();
                this.shutdownTcs = new CancellationTokenSource();

                this.listenLoop = Task.Run(
                    () => this.WorkItemListenLoop(workItemStream, this.shutdownTcs.Token),
                    CancellationToken.None);
                break;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
            {
                this.logger.SidecarUnavailable(this.sidecarGrpcChannel.Target);

                await Task.Delay(TimeSpan.FromSeconds(5), startupCancelToken);
            }
        }
    }

    /// <inheritdoc cref="StartAsync(CancellationToken)" />
    /// <param name="timeout">The maximum time to wait for a connection to be established.</param>
    public async Task StartAsync(TimeSpan timeout)
    {
        using CancellationTokenSource cts = new(timeout);
        await this.StartAsync(cts.Token);
    }

    AsyncServerStreamingCall<P.WorkItem> Connect(CancellationToken cancellationToken)
    {
        return this.sidecarClient.GetWorkItems(new P.GetWorkItemsRequest(), cancellationToken: cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        // Cancelling the shutdownTcs causes the background processing to shutdown gracefully.
        this.shutdownTcs?.Cancel();

        // Wait for the listen loop to copmlete
        await (this.listenLoop?.WaitAsync(cancellationToken) ?? Task.CompletedTask);

        // TODO: Wait for any outstanding tasks to complete
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        // Shutdown with a default timeout of 30 seconds
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
        try
        {
            await this.StopAsync(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }

        GC.SuppressFinalize(this);
    }

    async Task WorkItemListenLoop(AsyncServerStreamingCall<P.WorkItem> workItemStream, CancellationToken shutdownToken)
    {
        bool reconnect = false;

        while (true)
        {
            try
            {
                if (reconnect)
                {
                    workItemStream = this.Connect(shutdownToken);
                }

                await foreach (P.WorkItem workItem in workItemStream.ResponseStream.ReadAllAsync(shutdownToken))
                {
                    if (workItem.RequestCase == P.WorkItem.RequestOneofCase.OrchestratorRequest)
                    {
                        this.RunBackgroundTask(workItem, () => this.OnRunOrchestratorAsync(workItem.OrchestratorRequest));
                    }
                    else if (workItem.RequestCase == P.WorkItem.RequestOneofCase.ActivityRequest)
                    {
                        this.RunBackgroundTask(workItem, () => this.OnRunActivityAsync(workItem.ActivityRequest));
                    }
                    else
                    {
                        this.logger.UnexpectedWorkItemType(workItem.RequestCase.ToString());
                    }
                }
            }
            catch (RpcException) when (shutdownToken.IsCancellationRequested)
            {
                // Worker is shutting down - let the method exit gracefully
                break;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                // Sidecar is shutting down - retry
                this.logger.TaskHubWorkerDisconnected(this.sidecarGrpcChannel.Target);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
            {
                // Sidecar is down - keep retrying
                this.logger.SidecarUnavailable(this.sidecarGrpcChannel.Target);
            }
            catch (Exception ex)
            {
                // Unknown failure - retry?
                this.logger.UnexpectedError(instanceId: string.Empty, details: ex.ToString());
            }

            try
            {
                // CONSIDER: Exponential backoff
                await Task.Delay(TimeSpan.FromSeconds(5), shutdownToken);
            }
            catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
            {
                // Worker is shutting down - let the method exit gracefully
                break;
            }

            reconnect = true;
        }
    }

    void RunBackgroundTask(P.WorkItem? workItem, Func<Task> handler)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await handler();
            }
            catch (OperationCanceledException)
            {
                // Shutting down - ignore
            }
            catch (Exception e)
            {
                string instanceId =
                    workItem?.OrchestratorRequest?.InstanceId ??
                    workItem?.ActivityRequest?.OrchestrationInstance?.InstanceId ??
                    string.Empty;
                this.logger.UnexpectedError(instanceId, e.ToString());
            }
        });
    }

    async Task OnRunOrchestratorAsync(P.OrchestratorRequest request)
    {
        OrchestrationRuntimeState runtimeState = BuildRuntimeState(request);
        TaskName name = new(runtimeState.Name, runtimeState.Version);

        this.logger.ReceivedOrchestratorRequest(name, request.InstanceId);

        if (!this.orchestrators.TryGetValue(name, out Func<TaskOrchestration>? factory) || factory == null)
        {
            // TODO: Need a way to send the response back to the sidecar
            throw new ArgumentException($"No task orchestration named '{name}' was found.", nameof(request));
        }

        TaskOrchestration orchestrator = factory.Invoke();
        TaskOrchestrationExecutor executor = new(runtimeState, orchestrator, BehaviorOnContinueAsNew.Carryover);
        OrchestratorExecutionResult result;

        try
        {
            result = await executor.ExecuteAsync();
        }
        catch (Exception applicationException)
        {
            this.logger.OrchestratorFailed(name, request.InstanceId, applicationException.ToString());
            result = this.CreateOrchestrationFailedActionResult(applicationException);
        }

        // TODO: This is a workaround that allows us to change how the exception is presented to the user.
        //       Need to move this workaround into DurableTask.Core as a breaking change.
        if (result.Actions.FirstOrDefault(a => a.OrchestratorActionType == OrchestratorActionType.OrchestrationComplete) is OrchestrationCompleteOrchestratorAction completedAction &&
            completedAction.OrchestrationStatus == OrchestrationStatus.Failed &&
            !string.IsNullOrEmpty(completedAction.Details))
        {
            completedAction.Result = SdkUtils.GetSerializedErrorPayload(
                this.dataConverter,
                "The orchestrator failed with an unhandled exception.",
                completedAction.Details);
        }

        P.OrchestratorResponse response = ProtoUtils.ConstructOrchestratorResponse(
            request.InstanceId,
            result.CustomStatus,
            result.Actions);

        this.logger.SendingOrchestratorResponse(name, response.InstanceId, response.Actions.Count);
        await this.sidecarClient.CompleteOrchestratorTaskAsync(response);
    }

    OrchestratorExecutionResult CreateOrchestrationFailedActionResult(Exception e)
    {
        return new OrchestratorExecutionResult
        {
            Actions = new[]
            {
                new OrchestrationCompleteOrchestratorAction
                {
                    Id = -1,
                    OrchestrationStatus = OrchestrationStatus.Failed,
                    Result = SdkUtils.GetSerializedErrorPayload(
                        this.dataConverter,
                        "The orchestrator failed with an unhandled exception.",
                        e),
                },
            },
        };
    }

    string CreateActivityFailedOutput(Exception e, string? message = null)
    {
        return SdkUtils.GetSerializedErrorPayload(
            this.dataConverter,
            message ?? "The activity failed with an unhandled exception.",
            e);
    }

    async Task OnRunActivityAsync(P.ActivityRequest request)
    {
        OrchestrationInstance instance = ProtoUtils.ConvertOrchestrationInstance(request.OrchestrationInstance);
        string rawInput = request.Input;

        int inputSize = rawInput != null ? Encoding.UTF8.GetByteCount(rawInput) : 0;
        this.logger.ReceivedActivityRequest(request.Name, request.TaskId, instance.InstanceId, inputSize);

        TaskName name = new(request.Name, request.Version);
        if (!this.activities.TryGetValue(name, out Func<TaskActivity>? factory) || factory == null)
        {
            // TODO: Send a retryable failure response back to the worker instead of throwing
            throw new ArgumentException($"No task activity named '{name}' was found.", nameof(request));
        }

        TaskContext innerContext = new(instance);
        TaskActivity activity = factory.Invoke();

        string output;
        try
        {
            output = await activity.RunAsync(innerContext, request.Input);
        }
        catch (Exception applicationException)
        {
            output = this.CreateActivityFailedOutput(
                applicationException,
                $"The activity '{name}#{request.TaskId}' failed with an unhandled exception.");
        }

        int outputSize = output != null ? Encoding.UTF8.GetByteCount(output) : 0;
        this.logger.SendingActivityResponse(name, request.TaskId, instance.InstanceId, outputSize);

        P.ActivityResponse response = ProtoUtils.ConstructActivityResponse(
            instance.InstanceId,
            request.TaskId,
            output);
        await this.sidecarClient.CompleteActivityTaskAsync(response);
    }

    static OrchestrationRuntimeState BuildRuntimeState(P.OrchestratorRequest request)
    {
        IEnumerable<HistoryEvent> pastEvents = request.PastEvents.Select(ProtoUtils.ConvertHistoryEvent);
        IEnumerable<HistoryEvent> newEvents = request.NewEvents.Select(ProtoUtils.ConvertHistoryEvent);

        // Reconstruct the orchestration state in a way that correctly distinguishes new events from past events
        var runtimeState = new OrchestrationRuntimeState(pastEvents.ToList());
        foreach (HistoryEvent e in newEvents)
        {
            // AddEvent() puts events into the NewEvents list.
            runtimeState.AddEvent(e);
        }

        if (runtimeState.ExecutionStartedEvent == null)
        {
            // TODO: What's the right way to handle this? Callback to the sidecar with a retriable error request?
            throw new InvalidOperationException("The provided orchestration history was incomplete");
        }

        return runtimeState;
    }

    public static Builder CreateBuilder() => new();

    public sealed class Builder : ITaskOrchestrationBuilder
    {
        internal ImmutableDictionary<TaskName, Func<TaskActivity>>.Builder activitiesBuilder =
            ImmutableDictionary.CreateBuilder<TaskName, Func<TaskActivity>>();

        internal ImmutableDictionary<TaskName, Func<TaskOrchestration>>.Builder orchestratorsBuilder =
            ImmutableDictionary.CreateBuilder<TaskName, Func<TaskOrchestration>>();

        internal ILoggerFactory loggerFactory = NullLoggerFactory.Instance;
        internal string address = "http://127.0.0.1:4001";
        internal IDataConverter dataConverter = SdkUtils.DefaultDataConverter;

        internal Builder()
        {
        }

        public TaskHubGrpcWorker Build() => new(this);

        public Builder AddTaskOrchestrator(
            TaskName name,
            Func<TaskOrchestrationContext, Task> implementation)
        {
            return this.AddTaskOrchestrator<object?>(name, async ctx =>
            {
                await implementation(ctx);
                return null;
            });
        }

        public Builder AddTaskOrchestrator<T>(
            TaskName name,
            Func<TaskOrchestrationContext, Task<T>> implementation)
        {
            if (name == default)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (implementation == null)
            {
                throw new ArgumentNullException(nameof(implementation));
            }

            if (this.orchestratorsBuilder.ContainsKey(name))
            {
                throw new ArgumentException($"A task orchestrator named '{name}' is already added.", nameof(name));
            }

            this.orchestratorsBuilder.Add(
                name,
                () => new TaskOrchestrationWrapper<T>(this, name, implementation));

            return this;
        }

        // TODO: Overloads for class-based factory types
        public Builder AddTaskActivity(
            TaskName name,
            Func<TaskActivityContext, object?> implementation)
        {
            return this.AddTaskActivity(name, context => Task.FromResult(implementation(context)));
        }

        public Builder AddTaskActivity(
            TaskName name,
            Func<TaskActivityContext, Task> implementation)
        {
            return this.AddTaskActivity<object?>(name, async context =>
            {
                await implementation(context);
                return null;
            });
        }

        public Builder AddTaskActivity<T>(
            TaskName name,
            Func<TaskActivityContext, Task<T>> implementation)
        {
            if (name == default)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (implementation == null)
            {
                throw new ArgumentNullException(nameof(implementation));
            }

            if (this.activitiesBuilder.ContainsKey(name))
            {
                throw new ArgumentException($"A task activity named '{name}' is already added.", nameof(name));
            }

            this.activitiesBuilder.Add(
                name,
                () => new TaskActivityWrapper<T>(this, name, implementation));
            return this;
        }

        public Builder UseLoggerFactory(ILoggerFactory loggerFactory)
        {
            this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            return this;
        }

        public Builder UseAddress(string address)
        {
            this.address = SdkUtils.ValidateAddress(address);
            return this;
        }

        public Builder UseDataConverter(IDataConverter dataConverter)
        {
            this.dataConverter = dataConverter ?? throw new ArgumentNullException(nameof(dataConverter));
            return this;
        }

        /// <inheritdoc/>
        ITaskOrchestrationBuilder ITaskOrchestrationBuilder.AddTaskOrchestrator<T>(TaskName name, Func<TaskOrchestrationContext, Task<T>> implementation)
        {
            return this.AddTaskOrchestrator(name, implementation);
        }

        /// <inheritdoc/>
        ITaskOrchestrationBuilder ITaskOrchestrationBuilder.AddTaskOrchestrator(TaskName name, Func<TaskOrchestrationContext, Task> implementation)
        {
            return this.AddTaskOrchestrator(name, implementation);
        }

        /// <inheritdoc/>
        ITaskOrchestrationBuilder ITaskOrchestrationBuilder.AddTaskActivity<T>(TaskName name, Func<TaskActivityContext, Task<T>> implementation)
        {
            return this.AddTaskActivity(name, implementation);
        }

        /// <inheritdoc/>
        ITaskOrchestrationBuilder ITaskOrchestrationBuilder.AddTaskActivity(TaskName name, Func<TaskActivityContext, object?> implementation)
        {
            return this.AddTaskActivity(name, implementation);
        }

        /// <inheritdoc/>
        ITaskOrchestrationBuilder ITaskOrchestrationBuilder.AddTaskActivity(TaskName name, Func<TaskActivityContext, Task> implementation)
        {
            return this.AddTaskActivity(name, implementation);
        }
    }

    sealed class TaskOrchestrationWrapper<TOutput> : TaskOrchestration
    {
        readonly TaskName name;
        readonly Func<TaskOrchestrationContext, Task<TOutput>> wrappedImplementation;
        readonly IDataConverter dataConverter;

        TaskOrchestrationContextWrapper? wrapperContext;

        public TaskOrchestrationWrapper(
            Builder builder,
            TaskName name,
            Func<TaskOrchestrationContext, Task<TOutput>> wrappedImplementation)
        {
            this.dataConverter = builder.dataConverter;
            this.name = name;
            this.wrappedImplementation = wrappedImplementation;
        }

        public override async Task<string?> Execute(OrchestrationContext innerContext, string rawInput)
        {
            this.wrapperContext = new(innerContext, this.name, rawInput, this.dataConverter);

            // NOTE: If this throws, the error response will be generated by DurableTask.Core. However,
            //       it won't be consistent with our expected format. We currently work around this
            //       in the gRPC handling code, but ideally we wouldn't need this workaround.
            TOutput? output = await this.wrappedImplementation.Invoke(this.wrapperContext);

            // Return the output (if any) as a serialized string.
            return this.dataConverter.Serialize(output);
        }

        public override string? GetStatus()
        {
            return this.wrapperContext?.GetDeserializedCustomStatus();
        }

        public override void RaiseEvent(OrchestrationContext context, string name, string input)
        {
            this.wrapperContext?.CompleteExternalEvent(name, input);
        }

        sealed class TaskOrchestrationContextWrapper : TaskOrchestrationContext
        {
            readonly Dictionary<string, IEventSource> externalEventSources = new(StringComparer.OrdinalIgnoreCase);
            readonly NamedQueue<string> externalEventBuffer = new();

            readonly OrchestrationContext innerContext;
            readonly TaskName name;
            readonly string rawInput;
            readonly IDataConverter dataConverter;

            object? customStatus;

            public TaskOrchestrationContextWrapper(
                OrchestrationContext innerContext,
                TaskName name,
                string rawInput,
                IDataConverter dataConverter)
            {
                this.innerContext = innerContext;
                this.name = name;
                this.rawInput = rawInput;
                this.dataConverter = dataConverter;
            }

            public override TaskName Name => this.name;

            public override string InstanceId => this.innerContext.OrchestrationInstance.InstanceId;

            public override bool IsReplaying => this.innerContext.IsReplaying;

            public override DateTime CurrentDateTimeUtc => this.innerContext.CurrentUtcDateTime;

            public override T GetInput<T>()
            {
                if (this.rawInput == null)
                {
                    return default!;
                }

                return this.dataConverter.Deserialize<T>(this.rawInput)!;
            }

            public override Task<T> CallActivityAsync<T>(
                TaskName name,
                object? input = null,
                TaskOptions? options = null)
            {
                // TODO: Retry options
                return this.innerContext.ScheduleTask<T>(name.Name, name.Version, input);
            }

            public override Task<TResult> CallSubOrchestratorAsync<TResult>(
                TaskName orchestratorName,
                string? instanceId = null,
                object? input = null,
                TaskOptions? options = null)
            {
                if (options != null)
                {
                    throw new NotImplementedException($"{nameof(TaskOptions)} are not yet supported.");
                }

                // TODO: Support for retry options and custom deserialization via TaskOptions
                return this.innerContext.CreateSubOrchestrationInstance<TResult>(
                    orchestratorName.Name,
                    orchestratorName.Version,
                    instanceId ?? Guid.NewGuid().ToString("N"),
                    input);
            }

            public override Task CreateTimer(DateTime fireAt, CancellationToken cancellationToken)
            {
                return this.innerContext.CreateTimer<object>(fireAt, state: null!, cancellationToken);
            }

            public override Task<T> WaitForExternalEvent<T>(string eventName, CancellationToken cancellationToken = default)
            {
                // Return immediately if this external event has already arrived.
                if (this.externalEventBuffer.TryTake(eventName, out string? bufferedEventPayload))
                {
                    return Task.FromResult(this.dataConverter.Deserialize<T>(bufferedEventPayload));
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

            public void CompleteExternalEvent(string eventName, string rawEventPayload)
            {
                if (this.externalEventSources.TryGetValue(eventName, out IEventSource? waiter))
                {
                    object? value = this.dataConverter.Deserialize(rawEventPayload, waiter.EventType);

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

            public override void SetCustomStatus(object customStatus)
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

            internal string? GetDeserializedCustomStatus()
            {
                return this.dataConverter.Serialize(this.customStatus);
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

    sealed class TaskActivityWrapper<TOutput> : TaskActivity
    {
        readonly TaskName name;
        readonly Func<TaskActivityContext, Task<TOutput>> wrappedImplementation;

        readonly IDataConverter dataConverter;

        public TaskActivityWrapper(
            Builder builder,
            TaskName name,
            Func<TaskActivityContext, Task<TOutput>> implementation)
        {
            this.dataConverter = builder.dataConverter ?? new JsonDataConverter();
            this.name = name;
            this.wrappedImplementation = implementation;
        }

        public override async Task<string?> RunAsync(TaskContext coreContext, string rawInput)
        {
            string? sanitizedInput = StripArrayCharacters(rawInput);
            TaskActivityContextWrapper contextWrapper = new(coreContext, this.name, sanitizedInput, this.dataConverter);
            object? output = await this.wrappedImplementation.Invoke(contextWrapper);

            // Return the output (if any) as a serialized string.
            string? serializedOutput = output != null ? this.dataConverter.Serialize(output) : null;
            return serializedOutput;
        }

        static string? StripArrayCharacters(string? input)
        {
            if (input != null && input.StartsWith('[') && input.EndsWith(']'))
            {
                // Strip the outer bracket characters
                return input[1..^1];
            }

            return input;
        }

        // Not used/called
        public override string Run(TaskContext context, string input) => throw new NotImplementedException();

        sealed class TaskActivityContextWrapper : TaskActivityContext
        {
            readonly TaskContext innerContext;
            readonly TaskName name;
            readonly string? rawInput;
            readonly IDataConverter dataConverter;

            public TaskActivityContextWrapper(
                TaskContext taskContext,
                TaskName name,
                string? rawInput,
                IDataConverter dataConverter)
            {
                this.innerContext = taskContext;
                this.name = name;
                this.rawInput = rawInput;
                this.dataConverter = dataConverter;
            }

            public override TaskName Name => this.name;

            public override string InstanceId => this.innerContext.OrchestrationInstance.InstanceId;

            public override T GetInput<T>()
            {
                if (this.rawInput == null)
                {
                    return default!;
                }

                return this.dataConverter.Deserialize<T>(this.rawInput)!;
            }
        }
    }
}
