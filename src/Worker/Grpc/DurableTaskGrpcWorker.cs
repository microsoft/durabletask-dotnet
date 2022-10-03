// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Text;
using DurableTask.Core;
using DurableTask.Core.History;
using Grpc.Core;
using Microsoft.DurableTask.Options;
using Microsoft.DurableTask.Worker.Shims;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static Microsoft.DurableTask.Protobuf.TaskHubSidecarService;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Grpc;

// TODO: Rather than making this a top-level class, users should use TaskHubWorker.CreateBuilder().UseGrpc(address) or something similar to opt-into gRPC.
/// <summary>
/// Task hub worker that connects to a sidecar process over gRPC to execute orchestrator and activity events.
/// </summary>
public partial class DurableTaskGrpcWorker : IHostedService, IAsyncDisposable
{
    static readonly Google.Protobuf.WellKnownTypes.Empty EmptyMessage = new();

    readonly IServiceProvider services;
    readonly DurableTaskShimFactory shimFactory;
    readonly ILogger logger;
    readonly IConfiguration? configuration;
    readonly Channel sidecarGrpcChannel;
    readonly bool ownsChannel;
    readonly TaskHubSidecarServiceClient sidecarClient;

    readonly ImmutableDictionary<TaskName, Func<ITaskOrchestrator>> orchestrators;
    readonly ImmutableDictionary<TaskName, Func<IServiceProvider, ITaskActivity>> activities;

    CancellationTokenSource? shutdownTcs;
    Task? listenLoop;

    DurableTaskGrpcWorker(Builder builder)
    {
        this.services = builder.services ?? SdkUtils.EmptyServiceProvider.Instance;

        DataConverter dataConverter = builder.dataConverter
            ?? this.services.GetService<DataConverter>() ?? Converters.JsonDataConverter.Default;
        ILoggerFactory loggerFactory = builder.loggerFactory
            ?? this.services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
        TimerOptions timerOptions = builder.timerOptions
            ?? this.services.GetService<IOptions<TimerOptions>>()?.Value
            ?? new();
        this.shimFactory = new(dataConverter, loggerFactory, timerOptions);

        this.logger = SdkUtils.GetLogger(loggerFactory);
        this.configuration = builder.configuration ?? this.services.GetService<IConfiguration>();

        this.orchestrators = builder.taskProvider.orchestratorsBuilder.ToImmutable();
        this.activities = builder.taskProvider.activitiesBuilder.ToImmutable();

        if (builder.channel != null)
        {
            // Use the channel from the builder, which was given to us by the app (thus we don't own it and can't dispose it)
            this.sidecarGrpcChannel = builder.channel;
            this.ownsChannel = false;
        }
        else
        {
            // We have to create our own channel and are responsible for disposing it
            this.sidecarGrpcChannel = new Channel(
                builder.hostname ?? SdkUtils.GetSidecarHost(this.configuration),
                builder.port ?? SdkUtils.GetSidecarPort(this.configuration),
                ChannelCredentials.Insecure);
            this.ownsChannel = true;
        }

        this.sidecarClient = new TaskHubSidecarServiceClient(this.sidecarGrpcChannel);
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
            throw new InvalidOperationException($"This {nameof(DurableTaskGrpcWorker)} is already started.");
        }

        this.logger.StartingTaskHubWorker(this.sidecarGrpcChannel.Target);

        // Keep trying to connect until the caller cancels
        while (true)
        {
            try
            {
                AsyncServerStreamingCall<P.WorkItem>? workItemStream = await this.ConnectAsync(startupCancelToken);

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

    async Task<AsyncServerStreamingCall<P.WorkItem>> ConnectAsync(CancellationToken cancellationToken)
    {
        // Ping the sidecar to make sure it's up and listening.
        await this.sidecarClient.HelloAsync(EmptyMessage, cancellationToken: cancellationToken);
        this.logger.EstablishedWorkItemConnection();

        // Get the stream for receiving work-items
        return this.sidecarClient.GetWorkItems(new P.GetWorkItemsRequest(), cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Stops the current worker's listen loop, preventing any new orchestrator or activity events from being processed.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to use for cancelling the stop operation.</param>
    /// <returns>Returns a task that completes once the shutdown process has completed.</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        // Cancelling the shutdownTcs causes the background processing to shutdown gracefully.
        this.shutdownTcs?.Cancel();

        // Wait for the listen loop to complete
        await (this.listenLoop?.WaitAsync(cancellationToken) ?? Task.CompletedTask);

        // TODO: Wait for any outstanding tasks to complete
    }

    /// <inheritdoc/>
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

        if (this.ownsChannel)
        {
            await this.sidecarGrpcChannel.ShutdownAsync();
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
                    workItemStream = await this.ConnectAsync(shutdownToken);
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
                this.logger.SidecarDisconnected(this.sidecarGrpcChannel.Target);
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
        OrchestratorExecutionResult? result = null;
        P.TaskFailureDetails? failureDetails = null;
        TaskName name = new("(unknown)");

        try
        {
            OrchestrationRuntimeState runtimeState = BuildRuntimeState(request);
            name = new(runtimeState.Name, runtimeState.Version);

            this.logger.ReceivedOrchestratorRequest(
                name,
                request.InstanceId,
                runtimeState.PastEvents.Count,
                runtimeState.NewEvents.Count);

            TaskOrchestration orchestrator;
            if (this.orchestrators.TryGetValue(name, out Func<ITaskOrchestrator>? factory))
            {
                // Both the factory invocation and the ExecuteAsync could involve user code and need to be handled as part of try/catch.
                orchestrator = this.shimFactory.CreateOrchestration(name, factory.Invoke());
                TaskOrchestrationExecutor executor = new(
                    runtimeState,
                    orchestrator,
                    BehaviorOnContinueAsNew.Carryover,
                    ErrorPropagationMode.UseFailureDetails);
                result = executor.Execute();
            }
            else
            {
                failureDetails = new P.TaskFailureDetails
                {
                    ErrorType = "OrchestratorTaskNotFound",
                    ErrorMessage = $"No orchestrator task named '{name}' was found.",
                    IsNonRetriable = true,
                };
            }
        }
        catch (Exception unexpected)
        {
            // This is not expected: Normally TaskOrchestrationExecutor handles exceptions in user code.
            this.logger.OrchestratorFailed(name, request.InstanceId, unexpected.ToString());
            failureDetails = ProtoUtils.ToTaskFailureDetails(unexpected);
        }

        P.OrchestratorResponse response;
        if (result != null)
        {
            response = ProtoUtils.ConstructOrchestratorResponse(
                request.InstanceId,
                result.CustomStatus,
                result.Actions);
        }
        else
        {
            // This is the case for failures that happened *outside* the orchestrator executor
            response = new P.OrchestratorResponse
            {
                InstanceId = request.InstanceId,
                Actions =
                {
                    new P.OrchestratorAction
                    {
                        CompleteOrchestration = new P.CompleteOrchestrationAction
                        {
                            OrchestrationStatus = P.OrchestrationStatus.Failed,
                            FailureDetails = failureDetails,
                        },
                    },
                },
            };
        }

        this.logger.SendingOrchestratorResponse(
            name,
            response.InstanceId,
            response.Actions.Count,
            GetActionsListForLogging(response.Actions));

        await this.sidecarClient.CompleteOrchestratorTaskAsync(response);
    }

    static string GetActionsListForLogging(IReadOnlyList<P.OrchestratorAction> actions)
    {
        if (actions.Count == 0)
        {
            return string.Empty;
        }
        else if (actions.Count == 1)
        {
            return actions[0].OrchestratorActionTypeCase.ToString();
        }
        else
        {
            // Returns something like "ScheduleTask x5, CreateTimer x1,..."
            return string.Join(", ", actions
                .GroupBy(a => a.OrchestratorActionTypeCase)
                .Select(group => $"{group.Key} x{group.Count()}"));
        }
    }

    OrchestratorExecutionResult CreateOrchestrationFailedActionResult(Exception e)
    {
        return this.CreateOrchestrationFailedActionResult(
            message: "The orchestrator failed with an unhandled exception.",
            fullText: e.ToString());
    }

    OrchestratorExecutionResult CreateOrchestrationFailedActionResult(string message, string? fullText = null)
    {
        return OrchestratorExecutionResult.ForFailure(message, fullText);
    }

    async Task OnRunActivityAsync(P.ActivityRequest request)
    {
        OrchestrationInstance instance = ProtoUtils.ConvertOrchestrationInstance(request.OrchestrationInstance);
        string rawInput = request.Input;

        int inputSize = rawInput != null ? Encoding.UTF8.GetByteCount(rawInput) : 0;
        this.logger.ReceivedActivityRequest(request.Name, request.TaskId, instance.InstanceId, inputSize);

        TaskContext innerContext = new(instance);

        TaskName name = new(request.Name, request.Version);

        string? output = null;
        P.TaskFailureDetails? failureDetails = null;
        try
        {
            if (this.activities.TryGetValue(name, out Func<IServiceProvider, ITaskActivity>? factory))
            {
                // Both the factory invocation and the RunAsync could involve user code and need to be handled as part of try/catch.
                TaskActivity activity = this.shimFactory.CreateActivity(name, factory.Invoke(this.services));
                output = await activity.RunAsync(innerContext, request.Input);
            }
            else
            {
                failureDetails = new P.TaskFailureDetails
                {
                    ErrorType = "ActivityTaskNotFound",
                    ErrorMessage = $"No activity task named '{name}' was found.",
                    IsNonRetriable = true,
                };
            }
        }
        catch (Exception applicationException)
        {
            failureDetails = new P.TaskFailureDetails
            {
                ErrorType = applicationException.GetType().FullName,
                ErrorMessage = applicationException.Message,
                StackTrace = applicationException.StackTrace,
            };
        }

        int outputSizeInBytes = 0;
        if (failureDetails != null)
        {
            outputSizeInBytes = ProtoUtils.GetApproximateByteCount(failureDetails);
        }
        else if (output != null)
        {
            outputSizeInBytes = Encoding.UTF8.GetByteCount(output);
        }

        string successOrFailure = failureDetails != null ? "failure" : "success";
        this.logger.SendingActivityResponse(successOrFailure, name, request.TaskId, instance.InstanceId, outputSizeInBytes);

        P.ActivityResponse response = new()
        {
            InstanceId = instance.InstanceId,
            TaskId = request.TaskId,
            Result = output,
            FailureDetails = failureDetails,
        };

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
            // TODO: What's the right way to handle this? Callback to the sidecar with a retryable error request?
            throw new InvalidOperationException("The provided orchestration history was incomplete");
        }

        return runtimeState;
    }

    /// <summary>
    /// Creates a new instance of the <see cref="Builder"/> class, which can be used to construct a customized
    /// <see cref="DurableTaskGrpcWorker"/> object.
    /// </summary>
    /// <returns>Returns a new <see cref="Builder"/> object.</returns>
    public static Builder CreateBuilder() => new();
}
