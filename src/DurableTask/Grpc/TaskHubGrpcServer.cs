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
using DurableTask.Grpc;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static DurableTask.Protobuf.TaskHubClientService;
using DuplexStream = Grpc.Core.AsyncDuplexStreamingCall<DurableTask.Protobuf.InitOrExecutionResponse, DurableTask.Protobuf.ExecutionRequest>;
using P = DurableTask.Protobuf;

namespace DurableTask;

public class TaskHubGrpcServer : IAsyncDisposable
{
    readonly GrpcChannel workerGrpcChannel;
    readonly TaskHubClientServiceClient workerClient;
    readonly IDataConverter dataConverter;
    readonly ILogger logger;

    readonly ImmutableDictionary<TaskName, Func<TaskOrchestration>> orchestrators;
    readonly ImmutableDictionary<TaskName, Func<TaskActivity>> activities;

    readonly AsyncLock writeChannelLock = new();

    CancellationTokenSource? shutdownTcs;

    TaskHubGrpcServer(Builder builder)
    {
        this.workerGrpcChannel = GrpcChannel.ForAddress(builder.address);
        this.workerClient = new TaskHubClientServiceClient(this.workerGrpcChannel);
        this.dataConverter = builder.dataConverter;
        this.logger = SdkUtils.GetLogger(builder.loggerFactory);

        this.orchestrators = builder.orchestratorsBuilder.ToImmutable();
        this.activities = builder.activitiesBuilder.ToImmutable();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        this.logger.StartingTaskHubServer(this.workerGrpcChannel.Target);
        DuplexStream stream = await this.ConnectAsync(cancellationToken);

        this.shutdownTcs?.Dispose();
        this.shutdownTcs = new CancellationTokenSource();

        _ = Task.Run(() => this.AsyncListenLoop(stream, this.shutdownTcs.Token), CancellationToken.None);
    }

    public async Task<DuplexStream> ConnectAsync(CancellationToken cancellationToken)
    {
        // CONSIDER: Wrap this in a custom exception type to abstract away gRPC
        var stream = this.workerClient.ExecutionStream(cancellationToken: cancellationToken);
        await stream.RequestStream.WriteAsync(new P.InitOrExecutionResponse { ConnectionString = "*" });
        return stream;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        this.shutdownTcs?.Cancel();
        // TODO: Wait for shutdown to complete?
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await this.StopAsync();
        GC.SuppressFinalize(this);
    }

    async void AsyncListenLoop(DuplexStream? stream, CancellationToken shutdownToken)
    {
        while (true)
        {
            try
            {
                if (stream == null)
                {
                    // Re-establish the connection to the worker.
                    stream = await this.ConnectAsync(shutdownToken);
                }

                // Keep pulling requests from the worker until the worker shuts down.
                // If there's an exception reading from the stream, then we must reconnect the stream.
                // Exceptions must not escape this loop or else we'll miss notifications from the worker.
                await foreach (P.ExecutionRequest request in stream.ResponseStream.ReadAllAsync(shutdownToken))
                {
                    // Non-blocking request handler that also catches and logs any exceptions.
                    _ = this.ExceptionHandlingWrapper(request, async () =>
                    {
                        switch (request.RequestCase)
                        {
                            case P.ExecutionRequest.RequestOneofCase.OrchestratorRequest:
                                // This is a request to execute an orchestrator. The response is a list of side-effects
                                // (actions) that goes back to the worker. If there's an unhandled exception in the
                                // processing then we must return that back to the worker as an error response.
                                // TODO: Do we need support for transient/retriable errors?
                                P.OrchestratorResponse orchestratorResponse;
                                try
                                {
                                    orchestratorResponse = await this.OnRunOrchestratorAsync(request.OrchestratorRequest);
                                }
                                catch (Exception e)
                                {
                                    orchestratorResponse = ProtoUtils.ConstructOrchestratorResponse(
                                        request.OrchestratorRequest.InstanceId,
                                        this.CreateOrchestrationFailedActionResult(e));
                                }

                                using (await this.writeChannelLock.AcquireAsync())
                                {
                                    await stream.RequestStream.WriteAsync(new P.InitOrExecutionResponse { OrchestratorResponse = orchestratorResponse });
                                }
                                break;
                            case P.ExecutionRequest.RequestOneofCase.ActivityRequest:
                                // This is a request to execute an activity. The response is the output of the activity.
                                // If there's an unhandled exception in the processing then we must return a generic
                                // error response back to the worker.
                                // TODO: Do we need support for transient/retriable errors?
                                P.ActivityResponse activityResponse;
                                try
                                {
                                    activityResponse = await this.OnRunActivityAsync(request.ActivityRequest);
                                }
                                catch (Exception e)
                                {
                                    activityResponse = ProtoUtils.ConstructActivityResponse(
                                        taskId: request.ActivityRequest.TaskId,
                                        serializedOutput: this.CreateActivityFailedOutput(e));
                                }

                                using (await this.writeChannelLock.AcquireAsync())
                                {
                                    await stream.RequestStream.WriteAsync(new P.InitOrExecutionResponse { ActivityResponse = activityResponse });
                                }
                                break;
                            default:
                                // This request type isn't supported by this SDK.
                                this.logger.UnexpectedRequestType(request.RequestCase.ToString());
                                break;
                        }
                    });
                }
            }
            catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled)
            {
                // Notify the worker that we're done listening (if it's still there).
                if (stream != null)
                {
                    await stream.RequestStream.CompleteAsync();
                }

                break;
            }
            catch (RpcException e) when (e.StatusCode == StatusCode.Unavailable)
            {
                // Something broke our connection to the worker. Log the error and reconnect.
                this.logger.ConnectionFailed();
                stream = null;

                // CONSIDER: exponential backoff?
                await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None);
            }
            catch (Exception e)
            {
                this.logger.UnexpectedError(instanceId: string.Empty, e.ToString());

                // CONSIDER: exponential backoff?
                await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None);
            }
        }

        this.logger.TaskHubServerDisconnected(this.workerGrpcChannel.Target);
    }

    async Task ExceptionHandlingWrapper(P.ExecutionRequest? request, Func<Task> handler)
    {
        // No exceptions must be allowed to break out of this method
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
                request?.OrchestratorRequest?.InstanceId ?? 
                request?.ActivityRequest?.OrchestrationInstance?.InstanceId ??
                string.Empty;
            this.logger.UnexpectedError(instanceId, e.ToString());
        }
    }

    async Task<P.OrchestratorResponse> OnRunOrchestratorAsync(P.OrchestratorRequest request)
    {
        OrchestrationRuntimeState runtimeState = BuildRuntimeState(request);
        TaskName name = new(runtimeState.Name, runtimeState.Version);

        this.logger.ReceivedOrchestratorRequest(name, request.InstanceId);

        if (!this.orchestrators.TryGetValue(name, out Func<TaskOrchestration>? factory) || factory == null)
        {
            // TODO: Send a retryable failure response back to the worker instead of throwing
            throw new ArgumentException($"No task orchestration named '{name}' was found.", nameof(request));
        }

        TaskOrchestration orchestrator = factory.Invoke();
        TaskOrchestrationExecutor executor = new(runtimeState, orchestrator, BehaviorOnContinueAsNew.Carryover);
        IEnumerable<OrchestratorAction> nextActions;

        try
        {
            nextActions = await executor.ExecuteAsync();
        }
        catch (Exception applicationException)
        {
            this.logger.OrchestratorFailed(name, request.InstanceId, applicationException.ToString());
            nextActions = this.CreateOrchestrationFailedActionResult(applicationException);
        }

        // TODO: This is a workaround that allows us to change how the exception is presented to the user.
        //       Need to move this workaround into DurableTask.Core as a breaking change.
        if (nextActions.FirstOrDefault(a => a.OrchestratorActionType == OrchestratorActionType.OrchestrationComplete) is OrchestrationCompleteOrchestratorAction completedAction &&
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
            nextActions);

        this.logger.SendingOrchestratorResponse(name, response.InstanceId, response.Actions.Count);
        return response;
    }

    IEnumerable<OrchestratorAction> CreateOrchestrationFailedActionResult(Exception e)
    {
        yield return new OrchestrationCompleteOrchestratorAction
        {
            Id = -1,
            OrchestrationStatus = OrchestrationStatus.Failed,
            Result = SdkUtils.GetSerializedErrorPayload(
                this.dataConverter,
                "The orchestrator failed with an unhandled exception.",
                e),
        };
    }

    string CreateActivityFailedOutput(Exception e, string? message = null)
    {
        return SdkUtils.GetSerializedErrorPayload(
            this.dataConverter,
            message ?? "The activity failed with an unhandled exception.",
            e);
    }

    async Task<P.ActivityResponse> OnRunActivityAsync(P.ActivityRequest request)
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
            output = this.CreateActivityFailedOutput(applicationException, $"The activity '{name}#{request.TaskId}' failed with an unhandled exception.");
        }

        int outputSize = output != null ? Encoding.UTF8.GetByteCount(output) : 0;
        this.logger.SendingActivityResponse(name, request.TaskId, instance.InstanceId, outputSize);

        return ProtoUtils.ConstructActivityResponse(request.TaskId, output);
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
            // TODO: What's the right way to handle this?
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The provided orchestration history was incomplete"));
        }

        return runtimeState;
    }

    public static Builder CreateBuilder() => new();

    public sealed class Builder
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

        public TaskHubGrpcServer Build() => new(this);

        // TODO: Overloads for class-based factory types
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

        // TODO: Overloads for class-based factory types
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
