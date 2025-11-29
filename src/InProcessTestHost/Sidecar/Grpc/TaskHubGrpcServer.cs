// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using DurableTask.Core;
using DurableTask.Core.Command;
using DurableTask.Core.History;
using DurableTask.Core.Query;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.DurableTask.Testing.Sidecar.Dispatcher;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Testing.Sidecar.Grpc;

/// <summary>
/// gRPC server implementation for the TaskHub sidecar service.
/// </summary>
/// <remarks>
/// This class implements the gRPC service that handles communication between the durable task worker
/// and the orchestration service, managing work items and execution results.
/// </remarks>
public class TaskHubGrpcServer : P.TaskHubSidecarService.TaskHubSidecarServiceBase, ITaskExecutor, IDisposable // CA1001: Types owning disposable fields should be disposable
{
    static readonly Task<P.CompleteTaskResponse> EmptyCompleteTaskResponse = Task.FromResult(new P.CompleteTaskResponse());

    readonly ConcurrentDictionary<string, TaskCompletionSource<GrpcOrchestratorExecutionResult>> pendingOrchestratorTasks = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, TaskCompletionSource<ActivityExecutionResult>> pendingActivityTasks = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, PartialOrchestratorChunk> partialOrchestratorChunks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Helper class to accumulate partial orchestrator chunks.
    /// </summary>
    sealed class PartialOrchestratorChunk
    {
        public TaskCompletionSource<GrpcOrchestratorExecutionResult> TaskCompletionSource { get; set; } = null!;
        public List<OrchestratorAction> AccumulatedActions { get; } = new();
    }

    readonly ILogger log;
    readonly IOrchestrationService service;
    readonly IOrchestrationServiceClient client;
    readonly IHostApplicationLifetime hostLifetime;
    readonly IOptions<TaskHubGrpcServerOptions> options;
    readonly TaskHubDispatcherHost dispatcherHost;
    readonly IsConnectedSignal isConnectedSignal = new();
    readonly SemaphoreSlim sendWorkItemLock = new(initialCount: 1);
    readonly ConcurrentDictionary<string, List<P.HistoryEvent>> streamingPastEvents = new(StringComparer.OrdinalIgnoreCase);

    volatile bool supportsHistoryStreaming;

    // Initialized when a client connects to this service to receive work-item commands.
    IServerStreamWriter<P.WorkItem>? workerToClientStream;

    bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskHubGrpcServer"/> class.
    /// </summary>
    /// <param name="hostApplicationLifetime">The host application lifetime.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="service">The orchestration service.</param>
    /// <param name="client">The orchestration service client.</param>
    /// <param name="options">The server options.</param>
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
        this.log = loggerFactory.CreateLogger("Microsoft.DurableTask.Sidecar");
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

    /// <summary>
    /// Disposes the server resources.
    /// </summary>
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the server resources.
    /// </summary>
    /// <param name="disposing">Whether disposing from Dispose method.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!this.disposed && disposing)
        {
            this.sendWorkItemLock?.Dispose();
            this.isConnectedSignal?.Reset(); // Clean up the signal
            this.disposed = true;
        }
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

    /// <summary>
    /// Handles the Hello gRPC call.
    /// </summary>
    /// <param name="request">The empty request.</param>
    /// <param name="context">The server call context.</param>
    /// <returns>An empty response.</returns>
    public override Task<Empty> Hello(Empty request, ServerCallContext context) => Task.FromResult(new Empty());

    /// <summary>
    /// Creates a task hub.
    /// </summary>
    /// <param name="request">The create task hub request.</param>
    /// <param name="context">The server call context.</param>
    /// <returns>A create task hub response.</returns>
    public override Task<P.CreateTaskHubResponse> CreateTaskHub(P.CreateTaskHubRequest request, ServerCallContext context)
    {
        this.service.CreateAsync(request.RecreateIfExists);
        return Task.FromResult(new P.CreateTaskHubResponse());
    }

    /// <summary>
    /// Deletes a task hub.
    /// </summary>
    /// <param name="request">The delete task hub request.</param>
    /// <param name="context">The server call context.</param>
    /// <returns>A delete task hub response.</returns>
    public override Task<P.DeleteTaskHubResponse> DeleteTaskHub(P.DeleteTaskHubRequest request, ServerCallContext context)
    {
        this.service.DeleteAsync();
        return Task.FromResult(new P.DeleteTaskHubResponse());
    }

    /// <summary>
    /// Starts a new orchestration instance.
    /// </summary>
    /// <param name="request">The create instance request.</param>
    /// <param name="context">The server call context.</param>
    /// <returns>A create instance response.</returns>
    public override async Task<P.CreateInstanceResponse> StartInstance(P.CreateInstanceRequest request, ServerCallContext context)
    {
        OrchestrationInstance instance = new OrchestrationInstance
        {
            InstanceId = request.InstanceId ?? Guid.NewGuid().ToString("N"),
            ExecutionId = Guid.NewGuid().ToString(),
        };

        // TODO: Structured logging
        this.log.LogInformation("Creating a new instance with ID = {InstanceId}", instance.InstanceId);

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
                        ParentTraceContext = request.ParentTraceContext is not null
                            ? new(request.ParentTraceContext.TraceParent, request.ParentTraceContext.TraceState)
                            : null
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

    /// <summary>
    /// Raises an event to an orchestration instance.
    /// </summary>
    /// <param name="request">The raise event request.</param>
    /// <param name="context">The server call context.</param>
    /// <returns>A raise event response.</returns>
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

    /// <summary>
    /// Terminates an orchestration instance.
    /// </summary>
    /// <param name="request">The terminate request.</param>
    /// <param name="context">The server call context.</param>
    /// <returns>A terminate response.</returns>
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

    /// <summary>
    /// Gets an orchestration instance.
    /// </summary>
    /// <param name="request">The get instance request.</param>
    /// <param name="context">The server call context.</param>
    /// <returns>A get instance response.</returns>
    public override async Task<P.GetInstanceResponse> GetInstance(P.GetInstanceRequest request, ServerCallContext context)
    {
        OrchestrationState state = await this.client.GetOrchestrationStateAsync(request.InstanceId, executionId: null);
        if (state == null)
        {
            return new P.GetInstanceResponse() { Exists = false };
        }

        return CreateGetInstanceResponse(state, request);
    }

    /// <summary>
    /// Queries orchestration instances.
    /// </summary>
    /// <param name="request">The query instances request.</param>
    /// <param name="context">The server call context.</param>
    /// <returns>A query instances response.</returns>
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

    /// <summary>
    /// Purges orchestration instances.
    /// </summary>
    /// <param name="request">The purge instances request.</param>
    /// <param name="context">The server call context.</param>
    /// <returns>A purge instances response.</returns>
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
                    // CA2201: Use specific exception types
                    throw new NotSupportedException($"Unknown purge request type '{request.RequestCase}'.");
            }
            return ProtobufUtils.CreatePurgeInstancesResponse(result);
        }
        else
        {
            throw new NotSupportedException($"{this.client.GetType().Name} doesn't support purge operations.");
        }
    }

    /// <summary>
    /// Waits for an orchestration instance to start.
    /// </summary>
    /// <param name="request">The get instance request.</param>
    /// <param name="context">The server call context.</param>
    /// <returns>A get instance response.</returns>
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

    /// <summary>
    /// Waits for an orchestration instance to complete.
    /// </summary>
    /// <param name="request">The get instance request.</param>
    /// <param name="context">The server call context.</param>
    /// <returns>A get instance response.</returns>
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
                FailureDetails = request.GetInputsAndOutputs ? ProtobufUtils.GetFailureDetails(state.FailureDetails) : null,
                Tags = { state.Tags },
            },
        };
    }

    /// <summary>
    /// Suspends an orchestration instance.
    /// </summary>
    /// <param name="request">The suspend request.</param>
    /// <param name="context">The server call context.</param>
    /// <returns>A suspend response.</returns>
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

    /// <summary>
    /// Resumes an orchestration instance.
    /// </summary>
    /// <param name="request">The resume request.</param>
    /// <param name="context">The server call context.</param>
    /// <returns>A resume response.</returns>
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

    /// <summary>
    /// Restarts an orchestration instance.
    /// </summary>
    /// <param name="request">The restart instance request.</param>
    /// <param name="context">The server call context.</param>
    /// <returns>A restart instance response.</returns>
    public override async Task<P.RestartInstanceResponse> RestartInstance(P.RestartInstanceRequest request, ServerCallContext context)
    {
        try
        {
            // Get the original orchestration state
            IList<OrchestrationState> states = await this.client.GetOrchestrationStateAsync(request.InstanceId, false);

            if (states == null || states.Count == 0)
            {
                throw new RpcException(new Status(StatusCode.NotFound, $"An orchestration with the instanceId {request.InstanceId} was not found."));
            }

            OrchestrationState state = states[0];

            // Check if the state is null
            if (state == null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, $"An orchestration with the instanceId {request.InstanceId} was not found."));
            }

            string newInstanceId = request.RestartWithNewInstanceId ? Guid.NewGuid().ToString("N") : request.InstanceId;

            // Create a new orchestration instance
            OrchestrationInstance newInstance = new()
            {
                InstanceId = newInstanceId,
                ExecutionId = Guid.NewGuid().ToString("N"),
            };

            // Create an ExecutionStartedEvent with the original input
            ExecutionStartedEvent startedEvent = new(-1, state.Input)
            {
                Name = state.Name,
                Version = state.Version ?? string.Empty,
                OrchestrationInstance = newInstance,
            };

            TaskMessage taskMessage = new()
            {
                OrchestrationInstance = newInstance,
                Event = startedEvent,
            };

            await this.client.CreateTaskOrchestrationAsync(taskMessage);

            return new P.RestartInstanceResponse
            {
                InstanceId = newInstanceId,
            };
        }
        catch (RpcException)
        {
            // Re-throw RpcException as-is
            throw;
        }
        catch (Exception ex)
        {
            this.log.LogError(ex, "Error restarting orchestration instance {InstanceId}", request.InstanceId);
            throw new RpcException(new Status(StatusCode.Internal, ex.Message));
        }
    }

    /// <summary>
    /// Invoked by the remote SDK over gRPC when an orchestrator task (episode) is completed.
    /// </summary>
    /// <param name="request">Details about the orchestration execution, including the list of orchestrator actions.</param>
    /// <param name="context">Context for the server-side gRPC call.</param>
    /// <returns>Returns an empty ack back to the remote SDK that we've received the completion.</returns>
    public override Task<P.CompleteTaskResponse> CompleteOrchestratorTask(P.OrchestratorResponse request, ServerCallContext context)
    {
        if (request.IsPartial)
        {
            // This is a partial chunk - accumulate actions but don't complete yet
            PartialOrchestratorChunk partialChunk = this.partialOrchestratorChunks.GetOrAdd(
                request.InstanceId,
                _ =>
                {
                    // First chunk - get the TCS and initialize the partial chunk
                    if (!this.pendingOrchestratorTasks.TryGetValue(request.InstanceId, out TaskCompletionSource<GrpcOrchestratorExecutionResult>? tcs))
                    {
                        throw new RpcException(new Status(StatusCode.NotFound, $"Orchestration not found"));
                    }

                    return new PartialOrchestratorChunk
                    {
                        TaskCompletionSource = tcs,
                    };
                });

            // Accumulate actions from this chunk
            partialChunk.AccumulatedActions.AddRange(request.Actions.Select(ProtobufUtils.ToOrchestratorAction));

            return EmptyCompleteTaskResponse;
        }

        // This is the final chunk (or a single non-chunked response)
        if (this.partialOrchestratorChunks.TryRemove(request.InstanceId, out PartialOrchestratorChunk? existingPartialChunk))
        {
            // We've been accumulating chunks - combine with final chunk
            existingPartialChunk.AccumulatedActions.AddRange(request.Actions.Select(ProtobufUtils.ToOrchestratorAction));

            GrpcOrchestratorExecutionResult res = new()
            {
                Actions = existingPartialChunk.AccumulatedActions,
                CustomStatus = request.CustomStatus, // Use custom status from final chunk
            };

            // Remove the TCS from pending tasks and complete it
            this.pendingOrchestratorTasks.TryRemove(request.InstanceId, out _);
            existingPartialChunk.TaskCompletionSource.TrySetResult(res);

            return EmptyCompleteTaskResponse;
        }

        // Single non-chunked response (no partial chunks)
        if (!this.pendingOrchestratorTasks.TryRemove(
            request.InstanceId,
            out TaskCompletionSource<GrpcOrchestratorExecutionResult>? tcs))
        {
            // TODO: Log?
            // CA2201: Use specific exception types
            throw new RpcException(new Status(StatusCode.NotFound, $"Orchestration not found"));
        }

        GrpcOrchestratorExecutionResult result = new()
        {
            Actions = request.Actions.Select(ProtobufUtils.ToOrchestratorAction),
            CustomStatus = request.CustomStatus,
            OrchestrationActivitySpanId = request.OrchestrationTraceContext?.SpanID,
            OrchestrationActivityStartTime = request.OrchestrationTraceContext?.SpanStartTime?.ToDateTimeOffset(),
        };

        tcs.TrySetResult(result);

        return EmptyCompleteTaskResponse;
    }

    /// <summary>
    /// Invoked by the remote SDK over gRPC when an activity task (episode) is completed.
    /// </summary>
    /// <param name="request">Details about the completed activity task, including the output.</param>
    /// <param name="context">Context for the server-side gRPC call.</param>
    /// <returns>Returns an empty ack back to the remote SDK that we've received the completion.</returns>
    public override Task<P.CompleteTaskResponse> CompleteActivityTask(P.ActivityResponse request, ServerCallContext context)
    {
        string taskIdKey = GetTaskIdKey(request.InstanceId, request.TaskId);
        if (!this.pendingActivityTasks.TryRemove(taskIdKey, out TaskCompletionSource<ActivityExecutionResult>? tcs))
        {
            // TODO: Log?
            // CA2201: Use specific exception types
            throw new RpcException(new Status(StatusCode.NotFound, $"Activity not found"));
        }

        HistoryEvent resultEvent;
        if (request.FailureDetails == null)
        {
            resultEvent = new TaskCompletedEvent(-1, request.TaskId, request.Result);
        }
        else
        {
            resultEvent = new TaskFailedEvent(
                eventId: -1,
                taskScheduledId: request.TaskId,
                reason: null,
                details: null,
                failureDetails: ProtobufUtils.GetFailureDetails(request.FailureDetails));
        }

        tcs.TrySetResult(new ActivityExecutionResult { ResponseEvent = resultEvent });
        return EmptyCompleteTaskResponse;
    }

    /// <summary>
    /// Gets work items from the server.
    /// </summary>
    /// <param name="request">The get work items request.</param>
    /// <param name="responseStream">The response stream.</param>
    /// <param name="context">The server call context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public override async Task GetWorkItems(P.GetWorkItemsRequest request, IServerStreamWriter<P.WorkItem> responseStream, ServerCallContext context)
    {
        // Record whether the client supports history streaming
        this.supportsHistoryStreaming = request.Capabilities.Contains(P.WorkerCapability.HistoryStreaming);
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
                retryCount++; // Fix the increment
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

    /// <inheritdoc/>
    public override async Task StreamInstanceHistory(P.StreamInstanceHistoryRequest request, IServerStreamWriter<P.HistoryChunk> responseStream, ServerCallContext context)
    {
        if (this.streamingPastEvents.TryGetValue(request.InstanceId, out List<P.HistoryEvent>? pastEvents))
        {
            const int MaxChunkBytes = 256 * 1024; // 256KB per chunk to simulate chunked streaming
            int currentSize = 0;
            P.HistoryChunk chunk = new();

            foreach (P.HistoryEvent e in pastEvents)
            {
                int eventSize = e.CalculateSize();
                if (currentSize > 0 && currentSize + eventSize > MaxChunkBytes)
                {
                    await responseStream.WriteAsync(chunk);
                    chunk = new P.HistoryChunk();
                    currentSize = 0;
                }

                chunk.Events.Add(e);
                currentSize += eventSize;
            }

            if (chunk.Events.Count > 0)
            {
                await responseStream.WriteAsync(chunk);
            }
        }
    }

    /// <summary>
    /// Invoked by the <see cref="TaskHubDispatcherHost"/> when a work item is available, proxies the call to execute an orchestrator over a gRPC channel.
    /// </summary>
    /// <inheritdoc />
    async Task<GrpcOrchestratorExecutionResult> ITaskExecutor.ExecuteOrchestrator(
        OrchestrationInstance instance,
        IEnumerable<HistoryEvent> pastEvents,
        IEnumerable<HistoryEvent> newEvents)
    {
        ExecutionStartedEvent? executionStartedEvent = pastEvents.OfType<ExecutionStartedEvent>().FirstOrDefault();

        P.OrchestrationTraceContext? orchestrationTraceContext = executionStartedEvent?.ParentTraceContext?.SpanId is not null
            ? new P.OrchestrationTraceContext
            {
                SpanID = executionStartedEvent.ParentTraceContext.SpanId,
                SpanStartTime = executionStartedEvent.ParentTraceContext.ActivityStartTime?.ToTimestamp(),
            }
            : null;

        // Create a task completion source that represents the async completion of the orchestrator execution.
        // This must be done before we start the orchestrator execution.
        TaskCompletionSource<GrpcOrchestratorExecutionResult> tcs =
            this.CreateTaskCompletionSourceForOrchestrator(instance.InstanceId);

        try
        {
            P.OrchestratorRequest orkRequest = new P.OrchestratorRequest
            {
                InstanceId = instance.InstanceId,
                ExecutionId = instance.ExecutionId,
                NewEvents = { newEvents.Select(ProtobufUtils.ToHistoryEventProto) },
                OrchestrationTraceContext = orchestrationTraceContext,
            };

            // Decide whether to stream based on total size of past events (> 1MiB)
            List<P.HistoryEvent> protoPastEvents = pastEvents.Select(ProtobufUtils.ToHistoryEventProto).ToList();
            int totalBytes = 0;
            foreach (P.HistoryEvent ev in protoPastEvents)
            {
                totalBytes += ev.CalculateSize();
            }

            if (this.supportsHistoryStreaming && totalBytes > (1024))
            {
                orkRequest.RequiresHistoryStreaming = true;
                // Store past events to serve via StreamInstanceHistory
                this.streamingPastEvents[instance.InstanceId] = protoPastEvents;
            }
            else
            {
                // Embed full history in the work item
                orkRequest.PastEvents.AddRange(protoPastEvents);
            }

            await this.SendWorkItemToClientAsync(new P.WorkItem
            {
                OrchestratorRequest = orkRequest,
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
                    ParentTraceContext = activityEvent.ParentTraceContext is not null
                        ? new()
                        {
                            TraceParent = activityEvent.ParentTraceContext.TraceParent,
                            TraceState = activityEvent.ParentTraceContext.TraceState,
                        }
                        : null,
                },
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
                // CA2201: Use specific exception types
                throw new InvalidOperationException("TODO: No client is connected! Need to wait until a client connects before executing!");
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

    TaskCompletionSource<GrpcOrchestratorExecutionResult> CreateTaskCompletionSourceForOrchestrator(string instanceId)
    {
        TaskCompletionSource<GrpcOrchestratorExecutionResult> tcs = new();
        this.pendingOrchestratorTasks.TryAdd(instanceId, tcs);
        return tcs;
    }

    void RemoveOrchestratorTaskCompletionSource(string instanceId)
    {
        this.pendingOrchestratorTasks.TryRemove(instanceId, out _);
        this.partialOrchestratorChunks.TryRemove(instanceId, out _);
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
        return string.Concat(instanceId, "_", taskId.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Abandons a task activity work item.
    /// </summary>
    /// <param name="request">The abandon activity task request.</param>
    /// <param name="context">The server call context.</param>
    /// <returns>An abandon activity task response.</returns>
    public override Task<P.AbandonActivityTaskResponse> AbandonTaskActivityWorkItem(P.AbandonActivityTaskRequest request, ServerCallContext context)
    {
        return Task.FromResult<P.AbandonActivityTaskResponse>(new());
    }

    /// <summary>
    /// Abandons a task orchestration work item.
    /// </summary>
    /// <param name="request">The abandon orchestration task request.</param>
    /// <param name="context">The server call context.</param>
    /// <returns>An abandon orchestration task response.</returns>
    public override Task<P.AbandonOrchestrationTaskResponse> AbandonTaskOrchestratorWorkItem(P.AbandonOrchestrationTaskRequest request, ServerCallContext context)
    {
        return Task.FromResult<P.AbandonOrchestrationTaskResponse>(new());
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
