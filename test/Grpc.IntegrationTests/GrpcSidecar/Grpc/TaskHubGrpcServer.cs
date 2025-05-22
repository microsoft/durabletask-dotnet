// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics;
using DurableTask.Core;
using DurableTask.Core.History;
using DurableTask.Core.Query;
using Dapr.DurableTask.Sidecar.Dispatcher;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using P = Dapr.DurableTask.Protobuf;

namespace Dapr.DurableTask.Sidecar.Grpc;

public class TaskHubGrpcServer : P.TaskHubSidecarService.TaskHubSidecarServiceBase, ITaskExecutor
{
    static readonly Task<P.CompleteTaskResponse> EmptyCompleteTaskResponse = Task.FromResult(new P.CompleteTaskResponse());

    readonly ConcurrentDictionary<string, TaskCompletionSource<OrchestratorExecutionResult>> pendingOrchestratorTasks = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, TaskCompletionSource<ActivityExecutionResult>> pendingActivityTasks = new(StringComparer.OrdinalIgnoreCase);

    readonly ILogger log;
    readonly IOrchestrationService service;
    readonly IOrchestrationServiceClient client;
    readonly IHostApplicationLifetime hostLifetime;
    readonly IOptions<TaskHubGrpcServerOptions> options;
    readonly TaskHubDispatcherHost dispatcherHost;
    readonly IsConnectedSignal isConnectedSignal = new();
    readonly SemaphoreSlim sendWorkItemLock = new(initialCount: 1);

    // Initialized when a client connects to this service to receive work-item commands.
    IServerStreamWriter<P.WorkItem>? workerToClientStream;

    public TaskHubGrpcServer(
        IHostApplicationLifetime hostApplicationLifetime,
        ILoggerFactory loggerFactory,
        IOrchestrationService service,
        IOrchestrationServiceClient client,
        IOptions<TaskHubGrpcServerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(hostApplicationLifetime, nameof(hostApplicationLifetime));
        ArgumentNullException.ThrowIfNull(loggerFactory, nameof(loggerFactory));
        ArgumentNullException.ThrowIfNull(service, nameof(service));
        ArgumentNullException.ThrowIfNull(client, nameof(client));
        ArgumentNullException.ThrowIfNull(options, nameof(options));

        this.service = service;
        this.client = client;
        this.log = loggerFactory.CreateLogger("Dapr.DurableTask.Sidecar");
        this.dispatcherHost = new TaskHubDispatcherHost(
            loggerFactory,
            trafficSignal: this.isConnectedSignal,
            orchestrationService: service,
            taskExecutor: this);

        this.hostLifetime = hostApplicationLifetime;
        this.options = options;
        this.hostLifetime.ApplicationStarted.Register(this.OnApplicationStarted);
        this.hostLifetime.ApplicationStopping.Register(this.OnApplicationStopping);
    }

    async void OnApplicationStarted()
    {
        if (this.options.Value.Mode == TaskHubGrpcServerMode.ApiServerAndDispatcher)
        {
            // Wait for a client connection to be established before starting the dispatcher host.
            // This ensures we don't do any wasteful polling of resources if no clients are available to process events.
            await this.WaitForWorkItemClientConnection();
            await this.dispatcherHost.StartAsync(this.hostLifetime.ApplicationStopping);
        }
    }

    async void OnApplicationStopping()
    {
        if (this.options.Value.Mode == TaskHubGrpcServerMode.ApiServerAndDispatcher)
        {
            // Give a maximum of 60 minutes for outstanding tasks to complete.
            // REVIEW: Is this enough? What if there's an activity job that takes 4 hours to complete? Should this be configurable?
            using CancellationTokenSource timeoutCts = new(TimeSpan.FromMinutes(60));
            await this.dispatcherHost.StopAsync(timeoutCts.Token);
        }
    }

    /// <summary>
    /// Blocks until a remote client calls the <see cref="GetWorkItems"/> operation to start fetching work items.
    /// </summary>
    /// <returns>Returns a task that completes once a work-item client is connected.</returns>
    async Task WaitForWorkItemClientConnection()
    {
        Stopwatch waitTimeStopwatch = Stopwatch.StartNew();
        TimeSpan logInterval = TimeSpan.FromMinutes(1);

        try
        {
            while (!await this.isConnectedSignal.WaitAsync(logInterval, this.hostLifetime.ApplicationStopping))
            {
                this.log.WaitingForClientConnection(waitTimeStopwatch.Elapsed);
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    public override Task<Empty> Hello(Empty request, ServerCallContext context) => Task.FromResult(new Empty());

    public override Task<P.CreateTaskHubResponse> CreateTaskHub(P.CreateTaskHubRequest request, ServerCallContext context)
    {
        this.service.CreateAsync(request.RecreateIfExists);
        return Task.FromResult(new P.CreateTaskHubResponse());
    }

    public override Task<P.DeleteTaskHubResponse> DeleteTaskHub(P.DeleteTaskHubRequest request, ServerCallContext context)
    {
        this.service.DeleteAsync();
        return Task.FromResult(new P.DeleteTaskHubResponse());
    }

    public override async Task<P.CreateInstanceResponse> StartInstance(P.CreateInstanceRequest request, ServerCallContext context)
    {
        var instance = new OrchestrationInstance
        {
            InstanceId = request.InstanceId ?? Guid.NewGuid().ToString("N"),
            ExecutionId = Guid.NewGuid().ToString(),
        };

        // TODO: Structured logging
        this.log.LogInformation("Creating a new instance with ID = {instanceID}", instance.InstanceId);

        try
        {
            await this.client.CreateTaskOrchestrationAsync(
                new TaskMessage
                {
                    Event = new ExecutionStartedEvent(-1, request.Input)
                    {
                        Name = request.Name,
                        Version = request.Version,
                        OrchestrationInstance = instance,
                        Tags = request.Tags.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                    },
                    OrchestrationInstance = instance,
                });
        }
        catch (Exception e)
        {
            // TODO: Structured logging
            this.log.LogError(e, "An error occurred when trying to create a new instance");
            throw;
        }

        return new P.CreateInstanceResponse
        {
            InstanceId = instance.InstanceId,
        };
    }

    public override async Task<P.RaiseEventResponse> RaiseEvent(P.RaiseEventRequest request, ServerCallContext context)
    {
        try
        {
            await this.client.SendTaskOrchestrationMessageAsync(
                new TaskMessage
                {
                    Event = new EventRaisedEvent(-1, request.Input)
                    {
                        Name = request.Name,
                    },
                    OrchestrationInstance = new OrchestrationInstance
                    {
                        InstanceId = request.InstanceId,
                    },
                });
        }
        catch (Exception e)
        {
            // TODO: Structured logging
            this.log.LogError(e, "An error occurred when trying to raise an event.");
            throw;
        }

        // No fields in the response
        return new P.RaiseEventResponse();
    }

    public override async Task<P.TerminateResponse> TerminateInstance(P.TerminateRequest request, ServerCallContext context)
    {
        try
        {
            await this.client.ForceTerminateTaskOrchestrationAsync(
                request.InstanceId,
                request.Output);
        }
        catch (Exception e)
        {
            // TODO: Structured logging
            this.log.LogError(e, "An error occurred when trying to terminate an instance.");
            throw;
        }

        // No fields in the response
        return new P.TerminateResponse();
    }

    public override async Task<P.GetInstanceResponse> GetInstance(P.GetInstanceRequest request, ServerCallContext context)
    {
        OrchestrationState state = await this.client.GetOrchestrationStateAsync(request.InstanceId, executionId: null);
        if (state == null)
        {
            return new P.GetInstanceResponse() { Exists = false };
        }

        return CreateGetInstanceResponse(state, request);
    }

    public override async Task<P.QueryInstancesResponse> QueryInstances(P.QueryInstancesRequest request, ServerCallContext context)
    {
        if (this.client is IOrchestrationServiceQueryClient queryClient)
        {
            OrchestrationQuery query = ProtobufUtils.ToOrchestrationQuery(request);
            OrchestrationQueryResult result = await queryClient.GetOrchestrationWithQueryAsync(query, context.CancellationToken);
            return ProtobufUtils.CreateQueryInstancesResponse(result, request);
        }
        else
        {
            throw new NotSupportedException($"{this.client.GetType().Name} doesn't support query operations.");
        }
    }

    public override async Task<P.PurgeInstancesResponse> PurgeInstances(P.PurgeInstancesRequest request, ServerCallContext context)
    {
        if (this.client is IOrchestrationServicePurgeClient purgeClient)
        {
            PurgeResult result;
            switch (request.RequestCase)
            {
                case P.PurgeInstancesRequest.RequestOneofCase.InstanceId:
                    result = await purgeClient.PurgeInstanceStateAsync(request.InstanceId);
                    break;

                case P.PurgeInstancesRequest.RequestOneofCase.PurgeInstanceFilter:
                    PurgeInstanceFilter purgeInstanceFilter = ProtobufUtils.ToPurgeInstanceFilter(request);
                    result = await purgeClient.PurgeInstanceStateAsync(purgeInstanceFilter);
                    break;

                default:
                    throw new ArgumentException($"Unknown purge request type '{request.RequestCase}'.");
            }
            return ProtobufUtils.CreatePurgeInstancesResponse(result);
        }
        else
        {
            throw new NotSupportedException($"{this.client.GetType().Name} doesn't support purge operations.");
        }
    }

    public override async Task<P.GetInstanceResponse> WaitForInstanceStart(P.GetInstanceRequest request, ServerCallContext context)
    {
        while (true)
        {
            // Keep fetching the status until we get one of the states we care about
            OrchestrationState state = await this.client.GetOrchestrationStateAsync(request.InstanceId, executionId: null);
            if (state != null && state.OrchestrationStatus != OrchestrationStatus.Pending)
            {
                return CreateGetInstanceResponse(state, request);
            }

            // TODO: Backoff strategy if we're delaying for a long time.
            // The cancellation token is what will break us out of this loop if the orchestration
            // never leaves the "Pending" state.
            await Task.Delay(TimeSpan.FromMilliseconds(500), context.CancellationToken);
        }
    }

    public override async Task<P.GetInstanceResponse> WaitForInstanceCompletion(P.GetInstanceRequest request, ServerCallContext context)
    {
        OrchestrationState state = await this.client.WaitForOrchestrationAsync(
            request.InstanceId,
            executionId: null,
            timeout: Timeout.InfiniteTimeSpan,
            context.CancellationToken);

        return CreateGetInstanceResponse(state, request);
    }

    static P.GetInstanceResponse CreateGetInstanceResponse(OrchestrationState state, P.GetInstanceRequest request)
    {
        return new P.GetInstanceResponse
        {
            Exists = true,
            OrchestrationState = new P.OrchestrationState
            {
                InstanceId = state.OrchestrationInstance.InstanceId,
                Name = state.Name,
                OrchestrationStatus = (P.OrchestrationStatus)state.OrchestrationStatus,
                CreatedTimestamp = Timestamp.FromDateTime(state.CreatedTime),
                LastUpdatedTimestamp = Timestamp.FromDateTime(state.LastUpdatedTime),
                Input = request.GetInputsAndOutputs ? state.Input : null,
                Output = request.GetInputsAndOutputs ? state.Output : null,
                CustomStatus = request.GetInputsAndOutputs ? state.Status : null,
                FailureDetails = request.GetInputsAndOutputs ? GetFailureDetails(state.FailureDetails) : null,
                Tags = { state.Tags }
            }
        };
    }

    public override async Task<P.SuspendResponse> SuspendInstance(P.SuspendRequest request, ServerCallContext context)
    {
        TaskMessage taskMessage = new()
        {
            OrchestrationInstance = new OrchestrationInstance { InstanceId = request.InstanceId },
            Event = new ExecutionSuspendedEvent(-1, request.Reason),
        };

        await this.client.SendTaskOrchestrationMessageAsync(taskMessage);
        return new P.SuspendResponse();
    }

    public override async Task<P.ResumeResponse> ResumeInstance(P.ResumeRequest request, ServerCallContext context)
    {
        TaskMessage taskMessage = new()
        {
            OrchestrationInstance = new OrchestrationInstance { InstanceId = request.InstanceId },
            Event = new ExecutionResumedEvent(-1, request.Reason),
        };

        await this.client.SendTaskOrchestrationMessageAsync(taskMessage);
        return new P.ResumeResponse();
    }

    static P.TaskFailureDetails? GetFailureDetails(FailureDetails? failureDetails)
    {
        if (failureDetails == null)
        {
            return null;
        }

        return new P.TaskFailureDetails
        {
            ErrorType = failureDetails.ErrorType,
            ErrorMessage = failureDetails.ErrorMessage,
            StackTrace = failureDetails.StackTrace,
            InnerFailure = GetFailureDetails(failureDetails.InnerFailure),
        };
    }

    /// <summary>
    /// Invoked by the remote SDK over gRPC when an orchestrator task (episode) is completed.
    /// </summary>
    /// <param name="request">Details about the orchestration execution, including the list of orchestrator actions.</param>
    /// <param name="context">Context for the server-side gRPC call.</param>
    /// <returns>Returns an empty ack back to the remote SDK that we've received the completion.</returns>
    public override Task<P.CompleteTaskResponse> CompleteOrchestratorTask(P.OrchestratorResponse request, ServerCallContext context)
    {
        if (!this.pendingOrchestratorTasks.TryRemove(
            request.InstanceId,
            out TaskCompletionSource<OrchestratorExecutionResult>? tcs))
        {
            // TODO: Log?
            throw new RpcException(new Status(StatusCode.NotFound, $"Orchestration not found"));
        }

        OrchestratorExecutionResult result = new()
        {
            Actions = request.Actions.Select(ProtobufUtils.ToOrchestratorAction),
            CustomStatus = request.CustomStatus,
        };

        tcs.TrySetResult(result);

        return EmptyCompleteTaskResponse;
    }

    /// <summary>
    /// Invoked by the remote SDK over gRPC when an activity task (episode) is completed.
    /// </summary>
    /// <param name="response">Details about the completed activity task, including the output.</param>
    /// <param name="context">Context for the server-side gRPC call.</param>
    /// <returns>Returns an empty ack back to the remote SDK that we've received the completion.</returns>
    public override Task<P.CompleteTaskResponse> CompleteActivityTask(P.ActivityResponse response, ServerCallContext context)
    {
        string taskIdKey = GetTaskIdKey(response.InstanceId, response.TaskId);
        if (!this.pendingActivityTasks.TryRemove(taskIdKey, out TaskCompletionSource<ActivityExecutionResult>? tcs))
        {
            // TODO: Log?
            throw new RpcException(new Status(StatusCode.NotFound, $"Activity not found"));
        }

        HistoryEvent resultEvent;
        if (response.FailureDetails == null)
        {
            resultEvent = new TaskCompletedEvent(-1, response.TaskId, response.Result);
        }
        else
        {
            resultEvent = new TaskFailedEvent(
                eventId: -1,
                taskScheduledId: response.TaskId,
                reason: null,
                details: null,
                failureDetails: ProtobufUtils.GetFailureDetails(response.FailureDetails));
        }

        tcs.TrySetResult(new ActivityExecutionResult { ResponseEvent = resultEvent });
        return EmptyCompleteTaskResponse;
    }

    public override async Task GetWorkItems(P.GetWorkItemsRequest request, IServerStreamWriter<P.WorkItem> responseStream, ServerCallContext context)
    {
        // Use a lock to mitigate the race condition where we signal the dispatch host to start but haven't
        // yet saved a reference to the client response stream.
        lock (this.isConnectedSignal)
        {
            int retryCount = 0;
            while (!this.isConnectedSignal.Set())
            {
                // Retries are needed when a client (like a test suite) connects and disconnects rapidly, causing a race
                // condition where we don't reset the signal quickly enough to avoid ResourceExausted errors.
                if (retryCount <= 5)
                {
                    Thread.Sleep(10); // Can't use await inside the body of a lock statement so we have to block the thread
                }
                else
                {
                    throw new RpcException(new Status(StatusCode.ResourceExhausted, "Another client is already connected"));
                }
            }

            this.log.ClientConnected(context.Peer, context.Deadline);
            this.workerToClientStream = responseStream;
        }

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            this.log.ClientDisconnected(context.Peer);
        }
        finally
        {
            // Resetting this signal causes the dispatchers to stop fetching new work.
            this.isConnectedSignal.Reset();

            // Transition back to the "waiting for connection" state.
            // This background task is just to log "waiting for connection" messages.
            _ = Task.Run(this.WaitForWorkItemClientConnection);
        }
    }

    /// <summary>
    /// Invoked by the <see cref="TaskHubDispatcherHost"/> when a work item is available, proxies the call to execute an orchestrator over a gRPC channel.
    /// </summary>
    /// <inheritdoc />
    async Task<OrchestratorExecutionResult> ITaskExecutor.ExecuteOrchestrator(
        OrchestrationInstance instance,
        IEnumerable<HistoryEvent> pastEvents,
        IEnumerable<HistoryEvent> newEvents)
    {
        // Create a task completion source that represents the async completion of the orchestrator execution.
        // This must be done before we start the orchestrator execution.
        TaskCompletionSource<OrchestratorExecutionResult> tcs =
            this.CreateTaskCompletionSourceForOrchestrator(instance.InstanceId);

        try
        {
            await this.SendWorkItemToClientAsync(new P.WorkItem
            {
                OrchestratorRequest = new P.OrchestratorRequest
                {
                    InstanceId = instance.InstanceId,
                    ExecutionId = instance.ExecutionId,
                    NewEvents = { newEvents.Select(ProtobufUtils.ToHistoryEventProto) },
                    PastEvents = { pastEvents.Select(ProtobufUtils.ToHistoryEventProto) },
                }
            });
        }
        catch
        {
            // Remove the TaskCompletionSource that we just created
            this.RemoveOrchestratorTaskCompletionSource(instance.InstanceId);
            throw;
        }

        // The TCS will be completed on the message stream handler when it gets a response back from the remote process
        // TODO: How should we handle timeouts if the remote process never sends a response?
        //       Probably need to have a static timeout (e.g. 5 minutes).
        return await tcs.Task;
    }

    async Task<ActivityExecutionResult> ITaskExecutor.ExecuteActivity(OrchestrationInstance instance, TaskScheduledEvent activityEvent)
    {
        // Create a task completion source that represents the async completion of the activity.
        // This must be done before we start the activity execution.
        TaskCompletionSource<ActivityExecutionResult> tcs = this.CreateTaskCompletionSourceForActivity(
            instance.InstanceId,
            activityEvent.EventId);

        try
        {
            await this.SendWorkItemToClientAsync(new P.WorkItem
            {
                ActivityRequest = new P.ActivityRequest
                {
                    Name = activityEvent.Name,
                    Version = activityEvent.Version,
                    Input = activityEvent.Input,
                    TaskId = activityEvent.EventId,
                    OrchestrationInstance = new P.OrchestrationInstance
                    {
                        InstanceId = instance.InstanceId,
                        ExecutionId = instance.ExecutionId,
                    },
                }
            });
        }
        catch
        {
            // Remove the TaskCompletionSource that we just created
            this.RemoveActivityTaskCompletionSource(instance.InstanceId, activityEvent.EventId);
            throw;
        }

        // The TCS will be completed on the message stream handler when it gets a response back from the remote process.
        // TODO: How should we handle timeouts if the remote process never sends a response?
        //       Probably need a timeout feature for activities and/or a heartbeat API that activities
        //       can use to signal that they're still running.
        return await tcs.Task;
    }

    async Task SendWorkItemToClientAsync(P.WorkItem workItem)
    {
        IServerStreamWriter<P.WorkItem> outputStream;

        // Use a lock to mitigate the race condition where we signal the dispatch host to start but haven't
        // yet saved a reference to the client response stream.
        lock (this.isConnectedSignal)
        {
            outputStream = this.workerToClientStream ??
                throw new Exception("TODO: No client is connected! Need to wait until a client connects before executing!");
        }

        // The gRPC channel can only handle one message at a time, so we need to serialize access to it.
        await this.sendWorkItemLock.WaitAsync();
        try
        {
            await outputStream.WriteAsync(workItem);
        }
        finally
        {
            this.sendWorkItemLock.Release();
        }
    }

    TaskCompletionSource<OrchestratorExecutionResult> CreateTaskCompletionSourceForOrchestrator(string instanceId)
    {
        TaskCompletionSource<OrchestratorExecutionResult> tcs = new();
        this.pendingOrchestratorTasks.TryAdd(instanceId, tcs);
        return tcs;
    }

    void RemoveOrchestratorTaskCompletionSource(string instanceId)
    {
        this.pendingOrchestratorTasks.TryRemove(instanceId, out _);
    }

    TaskCompletionSource<ActivityExecutionResult> CreateTaskCompletionSourceForActivity(string instanceId, int taskId)
    {
        string taskIdKey = GetTaskIdKey(instanceId, taskId);
        TaskCompletionSource<ActivityExecutionResult> tcs = new();
        this.pendingActivityTasks.TryAdd(taskIdKey, tcs);
        return tcs;
    }

    void RemoveActivityTaskCompletionSource(string instanceId, int taskId)
    {
        string taskIdKey = GetTaskIdKey(instanceId, taskId);
        this.pendingActivityTasks.TryRemove(taskIdKey, out _);
    }

    static string GetTaskIdKey(string instanceId, int taskId)
    {
        return string.Concat(instanceId, "__", taskId.ToString());
    }

    /// <summary>
    /// A <see cref="ITrafficSignal"/> implementation that is used to control whether the task hub
    /// dispatcher can fetch new work-items, based on whether a client is currently connected.
    /// </summary>
    class IsConnectedSignal : ITrafficSignal
    {
        readonly AsyncManualResetEvent isConnectedEvent = new(isSignaled: false);

        /// <inheritdoc />
        public string WaitReason => "Waiting for a client to connect";

        /// <summary>
        /// Blocks the caller until the <see cref="Set"/> method is called, which means a client is connected.
        /// </summary>
        /// <inheritdoc />
        public Task<bool> WaitAsync(TimeSpan waitTime, CancellationToken cancellationToken)
        {
            return this.isConnectedEvent.WaitAsync(waitTime, cancellationToken);
        }

        /// <summary>
        /// Signals the dispatchers to start fetching new work-items.
        /// </summary>
        /// <returns>
        /// Returns <c>true</c> if the current thread transitioned the event to the "signaled" state;
        /// otherwise <c>false</c>, meaning some other thread already called <see cref="Set"/> on this signal.
        /// </returns>
        public bool Set() => this.isConnectedEvent.Set();

        /// <summary>
        /// Causes the dispatchers to stop fetching new work-items.
        /// </summary>
        public void Reset() => this.isConnectedEvent.Reset();
    }
}
