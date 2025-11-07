// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Text;
using DurableTask.Core;
using DurableTask.Core.Entities;
using DurableTask.Core.Entities.OperationFormat;
using DurableTask.Core.History;
using Microsoft.DurableTask.Abstractions;
using Microsoft.DurableTask.Entities;
using Microsoft.DurableTask.Tracing;
using Microsoft.DurableTask.Worker.Shims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static Microsoft.DurableTask.Protobuf.TaskHubSidecarService;
using ActivityStatusCode = System.Diagnostics.ActivityStatusCode;
using DTCore = DurableTask.Core;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc;

/// <summary>
/// The gRPC Durable Task worker.
/// </summary>
sealed partial class GrpcDurableTaskWorker
{
    class Processor
    {
        static readonly Google.Protobuf.WellKnownTypes.Empty EmptyMessage = new();

        readonly GrpcDurableTaskWorker worker;
        readonly TaskHubSidecarServiceClient client;
        readonly DurableTaskShimFactory shimFactory;
        readonly GrpcDurableTaskWorkerOptions.InternalOptions internalOptions;
        readonly DTCore.IExceptionPropertiesProvider? exceptionPropertiesProvider;
        [Obsolete("Experimental")]
        readonly IOrchestrationFilter? orchestrationFilter;

        public Processor(GrpcDurableTaskWorker worker, TaskHubSidecarServiceClient client, IOrchestrationFilter? orchestrationFilter = null, IExceptionPropertiesProvider? exceptionPropertiesProvider = null)
        {
            this.worker = worker;
            this.client = client;
            this.shimFactory = new DurableTaskShimFactory(this.worker.grpcOptions, this.worker.loggerFactory);
            this.internalOptions = this.worker.grpcOptions.Internal;
            this.orchestrationFilter = orchestrationFilter;
            this.exceptionPropertiesProvider = exceptionPropertiesProvider is not null
                ? new ExceptionPropertiesProviderAdapter(exceptionPropertiesProvider)
                : null;
        }

        ILogger Logger => this.worker.logger;

        public async Task ExecuteAsync(CancellationToken cancellation)
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    AsyncServerStreamingCall<P.WorkItem> stream = await this.ConnectAsync(cancellation);
                    await this.ProcessWorkItemsAsync(stream, cancellation);
                }
                catch (RpcException) when (cancellation.IsCancellationRequested)
                {
                    // Worker is shutting down - let the method exit gracefully
                    break;
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
                {
                    // Sidecar is shutting down - retry
                    this.Logger.SidecarDisconnected();
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
                {
                    // Sidecar is down - keep retrying
                    this.Logger.SidecarUnavailable();
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
                {
                    // We retry on a NotFound for several reasons:
                    // 1. It was the existing behavior through the UnexpectedError path.
                    // 2. A 404 can be returned for a missing task hub or authentication failure. Authentication takes
                    //    time to propagate so we should retry instead of making the user restart the application.
                    // 3. In some cases, a task hub can be created separately from the scheduler. If a worker is deployed
                    //    between the scheduler and task hub, it would need to be restarted to function.
                    this.Logger.TaskHubNotFound();
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    // Shutting down, lets exit gracefully.
                    break;
                }
                catch (Exception ex)
                {
                    // Unknown failure - retry?
                    this.Logger.UnexpectedError(ex, string.Empty);
                }

                try
                {
                    // CONSIDER: Exponential backoff
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellation);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    // Worker is shutting down - let the method exit gracefully
                    break;
                }
            }
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

        static P.TaskFailureDetails? EvaluateOrchestrationVersioning(DurableTaskWorkerOptions.VersioningOptions? versioning, string orchestrationVersion, out bool versionCheckFailed)
        {
            P.TaskFailureDetails? failureDetails = null;
            versionCheckFailed = false;
            if (versioning != null)
            {
                int versionComparison = TaskOrchestrationVersioningUtils.CompareVersions(orchestrationVersion, versioning.Version);

                switch (versioning.MatchStrategy)
                {
                    case DurableTaskWorkerOptions.VersionMatchStrategy.None:
                        // No versioning, breakout.
                        break;
                    case DurableTaskWorkerOptions.VersionMatchStrategy.Strict:
                        // Comparison of 0 indicates equality.
                        if (versionComparison != 0)
                        {
                            failureDetails = new P.TaskFailureDetails
                            {
                                ErrorType = "VersionMismatch",
                                ErrorMessage = $"The orchestration version '{orchestrationVersion}' does not match the worker version '{versioning.Version}'.",
                                IsNonRetriable = true,
                            };
                        }

                        break;
                    case DurableTaskWorkerOptions.VersionMatchStrategy.CurrentOrOlder:
                        // Comparison > 0 indicates the orchestration version is greater than the worker version.
                        if (versionComparison > 0)
                        {
                            failureDetails = new P.TaskFailureDetails
                            {
                                ErrorType = "VersionMismatch",
                                ErrorMessage = $"The orchestration version '{orchestrationVersion}' is greater than the worker version '{versioning.Version}'.",
                                IsNonRetriable = true,
                            };
                        }

                        break;
                    default:
                        // If there is a type of versioning we don't understand, it is better to treat it as a versioning failure.
                        failureDetails = new P.TaskFailureDetails
                        {
                            ErrorType = "VersionError",
                            ErrorMessage = $"The version match strategy '{orchestrationVersion}' is unknown.",
                            IsNonRetriable = true,
                        };
                        break;
                }

                versionCheckFailed = failureDetails != null;
            }

            return failureDetails;
        }

        async ValueTask<OrchestrationRuntimeState> BuildRuntimeStateAsync(
            P.OrchestratorRequest orchestratorRequest,
            ProtoUtils.EntityConversionState? entityConversionState,
            CancellationToken cancellation)
        {
            Func<P.HistoryEvent, HistoryEvent> converter = entityConversionState is null
                ? ProtoUtils.ConvertHistoryEvent
                : entityConversionState.ConvertFromProto;

            IEnumerable<HistoryEvent> pastEvents = [];
            if (orchestratorRequest.RequiresHistoryStreaming)
            {
                // Stream the remaining events from the remote service
                P.StreamInstanceHistoryRequest streamRequest = new()
                {
                    InstanceId = orchestratorRequest.InstanceId,
                    ExecutionId = orchestratorRequest.ExecutionId,
                    ForWorkItemProcessing = true,
                };

                using AsyncServerStreamingCall<P.HistoryChunk> streamResponse =
                    this.client.StreamInstanceHistory(streamRequest, cancellationToken: cancellation);

                await foreach (P.HistoryChunk chunk in streamResponse.ResponseStream.ReadAllAsync(cancellation))
                {
                    pastEvents = pastEvents.Concat(chunk.Events.Select(converter));
                }
            }
            else
            {
                // The history was already provided in the work item request
                pastEvents = orchestratorRequest.PastEvents.Select(converter);
            }

            List<HistoryEvent> pastEventsList = pastEvents.ToList();
            this.Logger.LogDebug(
                "{InstanceId}: Processing {Count} past event(s): {EventList}",
                orchestratorRequest.InstanceId,
                pastEventsList.Count,
                string.Join(", ", pastEventsList.Select(e => e.EventType.ToString())));

            IList<HistoryEvent> newEvents = orchestratorRequest.NewEvents.Select(converter).ToList();

            this.Logger.LogDebug(
                "{InstanceId}: Processing {Count} new event(s): {EventList}",
                orchestratorRequest.InstanceId,
                newEvents.Count,
                string.Join(", ", newEvents.Select(e => e.EventType.ToString())));


            // Reconstruct the orchestration state in a way that correctly distinguishes new events from past events
            var runtimeState = new OrchestrationRuntimeState(pastEventsList);
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

        async Task<AsyncServerStreamingCall<P.WorkItem>> ConnectAsync(CancellationToken cancellation)
        {
            await this.client!.HelloAsync(EmptyMessage, cancellationToken: cancellation);
            this.Logger.EstablishedWorkItemConnection();

            DurableTaskWorkerOptions workerOptions = this.worker.workerOptions;

            // Get the stream for receiving work-items
            return this.client!.GetWorkItems(
                new P.GetWorkItemsRequest
                {
                    MaxConcurrentActivityWorkItems =
                        workerOptions.Concurrency.MaximumConcurrentActivityWorkItems,
                    MaxConcurrentOrchestrationWorkItems =
                        workerOptions.Concurrency.MaximumConcurrentOrchestrationWorkItems,
                    MaxConcurrentEntityWorkItems =
                        workerOptions.Concurrency.MaximumConcurrentEntityWorkItems,
                    Capabilities = { P.WorkerCapability.HistoryStreaming },
                },
                cancellationToken: cancellation);
        }

        async Task ProcessWorkItemsAsync(AsyncServerStreamingCall<P.WorkItem> stream, CancellationToken cancellation)
        {
            // Create a new token source for timing out and a final token source that keys off of them both.
            // The timeout token is used to detect when we are no longer getting any messages, including health checks.
            // If this is the case, it signifies the connection has been dropped silently and we need to reconnect.
            using var timeoutSource = new CancellationTokenSource();
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(60));
            using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellation, timeoutSource.Token);

            while (!cancellation.IsCancellationRequested)
            {
                await foreach (P.WorkItem workItem in stream.ResponseStream.ReadAllAsync(tokenSource.Token))
                {
                    timeoutSource.CancelAfter(TimeSpan.FromSeconds(60));
                    if (workItem.RequestCase == P.WorkItem.RequestOneofCase.OrchestratorRequest)
                    {
                        this.RunBackgroundTask(
                            workItem,
                            () => this.OnRunOrchestratorAsync(
                                workItem.OrchestratorRequest,
                                workItem.CompletionToken,
                                cancellation),
                            cancellation);
                    }
                    else if (workItem.RequestCase == P.WorkItem.RequestOneofCase.ActivityRequest)
                    {
                        this.RunBackgroundTask(
                            workItem,
                            () => this.OnRunActivityAsync(
                                workItem.ActivityRequest,
                                workItem.CompletionToken,
                                cancellation),
                            cancellation);
                    }
                    else if (workItem.RequestCase == P.WorkItem.RequestOneofCase.EntityRequest)
                    {
                        this.RunBackgroundTask(
                            workItem,
                            () => this.OnRunEntityBatchAsync(workItem.EntityRequest.ToEntityBatchRequest(), cancellation),
                            cancellation);
                    }
                    else if (workItem.RequestCase == P.WorkItem.RequestOneofCase.EntityRequestV2)
                    {
                        workItem.EntityRequestV2.ToEntityBatchRequest(
                            out EntityBatchRequest batchRequest,
                            out List<P.OperationInfo> operationInfos);

                        this.RunBackgroundTask(
                             workItem,
                             () => this.OnRunEntityBatchAsync(
                                batchRequest,
                                cancellation,
                                workItem.CompletionToken,
                                operationInfos),
                             cancellation);
                    }
                    else if (workItem.RequestCase == P.WorkItem.RequestOneofCase.HealthPing)
                    {
                        // No-op
                    }
                    else
                    {
                        this.Logger.UnexpectedWorkItemType(workItem.RequestCase.ToString());
                    }
                }

                if (tokenSource.IsCancellationRequested || tokenSource.Token.IsCancellationRequested)
                {
                    // The token has cancelled, this means either:
                    // 1. The broader 'cancellation' was triggered, return here to start a graceful shutdown.
                    // 2. The timeoutSource was triggered, return here to trigger a reconnect to the backend.
                    if (!cancellation.IsCancellationRequested)
                    {
                        // Since the cancellation came from the timeout, log a warning.
                        this.Logger.ConnectionTimeout();
                    }

                    return;
                }
            }
        }

        void RunBackgroundTask(P.WorkItem? workItem, Func<Task> handler, CancellationToken cancellation)
        {
            // TODO: is Task.Run appropriate here? Should we have finer control over the tasks and their threads?
            _ = Task.Run(
                async () =>
            {
                try
                {
                    await handler();
                }
                catch (OperationCanceledException)
                {
                    // Shutting down - ignore
                }
                catch (Exception ex)
                {
                    string instanceId =
                        workItem?.OrchestratorRequest?.InstanceId ??
                        workItem?.ActivityRequest?.OrchestrationInstance?.InstanceId ??
                        workItem?.EntityRequest?.InstanceId ??
                        workItem?.EntityRequestV2?.InstanceId ??
                        string.Empty;
                    this.Logger.UnexpectedError(ex, instanceId);

                    if (workItem?.OrchestratorRequest != null)
                    {
                        try
                        {
                            this.Logger.AbandoningOrchestratorWorkItem(instanceId, workItem?.CompletionToken ?? string.Empty);
                            await this.client.AbandonTaskOrchestratorWorkItemAsync(
                                new P.AbandonOrchestrationTaskRequest
                                {
                                    CompletionToken = workItem?.CompletionToken,
                                },
                                cancellationToken: cancellation);
                            this.Logger.AbandonedOrchestratorWorkItem(instanceId, workItem?.CompletionToken ?? string.Empty);
                        }
                        catch (Exception abandonException)
                        {
                            this.Logger.UnexpectedError(abandonException, instanceId);
                        }
                    }
                    else if (workItem?.ActivityRequest != null)
                    {
                        try
                        {
                            this.Logger.AbandoningActivityWorkItem(
                                instanceId,
                                workItem.ActivityRequest.Name,
                                workItem.ActivityRequest.TaskId,
                                workItem?.CompletionToken ?? string.Empty);
                            await this.client.AbandonTaskActivityWorkItemAsync(
                                new P.AbandonActivityTaskRequest
                                {
                                    CompletionToken = workItem?.CompletionToken,
                                },
                                cancellationToken: cancellation);
                            this.Logger.AbandonedActivityWorkItem(
                                instanceId,
                                workItem.ActivityRequest.Name,
                                workItem.ActivityRequest.TaskId,
                                workItem?.CompletionToken ?? string.Empty);
                        }
                        catch (Exception abandonException)
                        {
                            this.Logger.UnexpectedError(abandonException, instanceId);
                        }
                    }
                    else if (workItem?.EntityRequest != null)
                    {
                        try
                        {
                            this.Logger.AbandoningEntityWorkItem(
                                workItem.EntityRequest.InstanceId,
                                workItem?.CompletionToken ?? string.Empty);
                            await this.client.AbandonTaskEntityWorkItemAsync(
                                new P.AbandonEntityTaskRequest
                                {
                                    CompletionToken = workItem?.CompletionToken,
                                },
                                cancellationToken: cancellation);
                            this.Logger.AbandonedEntityWorkItem(
                                workItem.EntityRequest.InstanceId,
                                workItem?.CompletionToken ?? string.Empty);
                        }
                        catch (Exception abandonException)
                        {
                            this.Logger.UnexpectedError(abandonException, workItem.EntityRequest.InstanceId);
                        }
                    }
                    else if (workItem?.EntityRequestV2 != null)
                    {
                        try
                        {
                            this.Logger.AbandoningEntityWorkItem(
                                workItem.EntityRequestV2.InstanceId,
                                workItem?.CompletionToken ?? string.Empty);
                            await this.client.AbandonTaskEntityWorkItemAsync(
                                new P.AbandonEntityTaskRequest
                                {
                                    CompletionToken = workItem?.CompletionToken,
                                },
                                cancellationToken: cancellation);
                            this.Logger.AbandonedEntityWorkItem(
                                workItem.EntityRequestV2.InstanceId,
                                workItem?.CompletionToken ?? string.Empty);
                        }
                        catch (Exception abandonException)
                        {
                            this.Logger.UnexpectedError(abandonException, workItem.EntityRequestV2.InstanceId);
                        }
                    }
                }
            });
        }

        async Task OnRunOrchestratorAsync(
            P.OrchestratorRequest request,
            string completionToken,
            CancellationToken cancellationToken)
        {
            var executionStartedEvent =
                request
                    .NewEvents
                    .Concat(request.PastEvents)
                    .Where(e => e.EventTypeCase == P.HistoryEvent.EventTypeOneofCase.ExecutionStarted)
                    .Select(e => e.ExecutionStarted)
                    .FirstOrDefault();

            Activity? traceActivity = TraceHelper.StartTraceActivityForOrchestrationExecution(
                executionStartedEvent,
                request.OrchestrationTraceContext);

            if (executionStartedEvent is not null)
            {
                P.HistoryEvent? GetSuborchestrationInstanceCreatedEvent(int eventId)
                {
                    var subOrchestrationEvent =
                        request
                            .PastEvents
                            .Where(x => x.EventTypeCase == P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCreated)
                            .FirstOrDefault(x => x.EventId == eventId);

                    return subOrchestrationEvent;
                }

                P.HistoryEvent? GetTaskScheduledEvent(int eventId)
                {
                    var taskScheduledEvent =
                        request
                            .PastEvents
                            .Where(x => x.EventTypeCase == P.HistoryEvent.EventTypeOneofCase.TaskScheduled)
                            .LastOrDefault(x => x.EventId == eventId);

                    return taskScheduledEvent;
                }

                foreach (var newEvent in request.NewEvents)
                {
                    switch (newEvent.EventTypeCase)
                    {
                        case P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCompleted:
                            {
                                P.HistoryEvent? subOrchestrationInstanceCreatedEvent =
                                    GetSuborchestrationInstanceCreatedEvent(
                                        newEvent.SubOrchestrationInstanceCompleted.TaskScheduledId);

                                TraceHelper.EmitTraceActivityForSubOrchestrationCompleted(
                                    request.InstanceId,
                                    subOrchestrationInstanceCreatedEvent,
                                    subOrchestrationInstanceCreatedEvent?.SubOrchestrationInstanceCreated);
                                break;
                            }

                        case P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceFailed:
                            {
                                P.HistoryEvent? subOrchestrationInstanceCreatedEvent =
                                    GetSuborchestrationInstanceCreatedEvent(
                                        newEvent.SubOrchestrationInstanceFailed.TaskScheduledId);

                                TraceHelper.EmitTraceActivityForSubOrchestrationFailed(
                                    request.InstanceId,
                                    subOrchestrationInstanceCreatedEvent,
                                    subOrchestrationInstanceCreatedEvent?.SubOrchestrationInstanceCreated,
                                    newEvent.SubOrchestrationInstanceFailed);
                                break;
                            }

                        case P.HistoryEvent.EventTypeOneofCase.TaskCompleted:
                            {
                                P.HistoryEvent? taskScheduledEvent =
                                    GetTaskScheduledEvent(newEvent.TaskCompleted.TaskScheduledId);

                                TraceHelper.EmitTraceActivityForTaskCompleted(
                                    request.InstanceId,
                                    taskScheduledEvent,
                                    taskScheduledEvent?.TaskScheduled);
                                break;
                            }

                        case P.HistoryEvent.EventTypeOneofCase.TaskFailed:
                            {
                                P.HistoryEvent? taskScheduledEvent =
                                    GetTaskScheduledEvent(newEvent.TaskFailed.TaskScheduledId);

                                TraceHelper.EmitTraceActivityForTaskFailed(
                                    request.InstanceId,
                                    taskScheduledEvent,
                                    taskScheduledEvent?.TaskScheduled,
                                    newEvent.TaskFailed);
                                break;
                            }

                        case P.HistoryEvent.EventTypeOneofCase.TimerFired:
                            TraceHelper.EmitTraceActivityForTimer(
                                request.InstanceId,
                                executionStartedEvent.Name,
                                newEvent.Timestamp.ToDateTime(),
                                newEvent.TimerFired);
                            break;
                    }
                }
            }

            OrchestratorExecutionResult? result = null;
            P.TaskFailureDetails? failureDetails = null;
            TaskName name = new("(unknown)");

            ProtoUtils.EntityConversionState? entityConversionState =
                this.internalOptions.ConvertOrchestrationEntityEvents
                ? new(this.internalOptions.InsertEntityUnlocksOnCompletion)
                : null;

            DurableTaskWorkerOptions.VersioningOptions? versioning = this.worker.workerOptions.Versioning;
            bool versionFailure = false;
            try
            {
                OrchestrationRuntimeState runtimeState = await this.BuildRuntimeStateAsync(
                    request,
                    entityConversionState,
                    cancellationToken);

                bool filterPassed = true;
                if (this.orchestrationFilter != null)
                {
                    filterPassed = await this.orchestrationFilter.IsOrchestrationValidAsync(
                        new OrchestrationFilterParameters
                        {
                            Name = runtimeState.Name,
                            Tags = runtimeState.Tags != null ? new Dictionary<string, string>(runtimeState.Tags) : null,
                        },
                        cancellationToken);
                }

                if (!filterPassed)
                {
                    this.Logger.AbandoningOrchestrationDueToOrchestrationFilter(request.InstanceId, completionToken);
                    await this.client.AbandonTaskOrchestratorWorkItemAsync(
                        new P.AbandonOrchestrationTaskRequest
                        {
                            CompletionToken = completionToken,
                        },
                        cancellationToken: cancellationToken);

                    return;
                }

                // If versioning has been explicitly set, we attempt to follow that pattern. If it is not set, we don't compare versions here.
                failureDetails = EvaluateOrchestrationVersioning(versioning, runtimeState.Version, out versionFailure);

                // Only continue with the work if the versioning check passed.
                if (failureDetails == null)
                {
                    name = new TaskName(runtimeState.Name);

                    this.Logger.ReceivedOrchestratorRequest(
                        name,
                        request.InstanceId,
                        runtimeState.PastEvents.Count,
                        runtimeState.NewEvents.Count);

                    await using AsyncServiceScope scope = this.worker.services.CreateAsyncScope();
                    if (this.worker.Factory.TryCreateOrchestrator(
                        name, scope.ServiceProvider, out ITaskOrchestrator? orchestrator))
                    {
                        // Both the factory invocation and the ExecuteAsync could involve user code and need to be handled
                        // as part of try/catch.
                        ParentOrchestrationInstance? parent = runtimeState.ParentInstance switch
                        {
                            ParentInstance p => new(new(p.Name), p.OrchestrationInstance.InstanceId),
                            _ => null,
                        };

                        TaskOrchestration shim = this.shimFactory.CreateOrchestration(name, orchestrator, parent);
                        TaskOrchestrationExecutor executor = new(
                            runtimeState,
                            shim,
                            BehaviorOnContinueAsNew.Carryover,
                            request.EntityParameters.ToCore(),
                            ErrorPropagationMode.UseFailureDetails,
                            this.exceptionPropertiesProvider);
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
            }
            catch (Exception unexpected)
            {
                // This is not expected: Normally TaskOrchestrationExecutor handles exceptions in user code.
                this.Logger.OrchestratorFailed(name, request.InstanceId, unexpected.ToString());
                failureDetails = unexpected.ToTaskFailureDetails(this.exceptionPropertiesProvider);
            }

            P.OrchestratorResponse response;
            if (result != null)
            {
                response = ProtoUtils.ConstructOrchestratorResponse(
                    request.InstanceId,
                    request.ExecutionId,
                    result.CustomStatus,
                    result.Actions,
                    completionToken,
                    entityConversionState,
                    traceActivity);
            }
            else if (versioning != null && failureDetails != null && versionFailure)
            {
                this.Logger.OrchestrationVersionFailure(versioning.FailureStrategy.ToString(), failureDetails.ErrorMessage);
                if (versioning.FailureStrategy == DurableTaskWorkerOptions.VersionFailureStrategy.Fail)
                {
                    response = new P.OrchestratorResponse
                    {
                        InstanceId = request.InstanceId,
                        CompletionToken = completionToken,
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
                else
                {
                    this.Logger.AbandoningOrchestrationDueToVersioning(request.InstanceId, completionToken);
                    await this.client.AbandonTaskOrchestratorWorkItemAsync(
                        new P.AbandonOrchestrationTaskRequest
                        {
                            CompletionToken = completionToken,
                        },
                        cancellationToken: cancellationToken);

                    return;
                }
            }
            else
            {
                // This is the case for failures that happened *outside* the orchestrator executor
                response = new P.OrchestratorResponse
                {
                    InstanceId = request.InstanceId,
                    CompletionToken = completionToken,
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

            var completeOrchestrationAction = response.Actions.FirstOrDefault(
                a => a.CompleteOrchestration is not null);

            if (completeOrchestrationAction is not null)
            {
                if (completeOrchestrationAction.CompleteOrchestration.OrchestrationStatus == P.OrchestrationStatus.Failed)
                {
                    traceActivity?.SetStatus(
                        ActivityStatusCode.Error,
                        completeOrchestrationAction.CompleteOrchestration.Result);
                }

                traceActivity?.SetTag(
                    Schema.Task.Status,
                    completeOrchestrationAction.CompleteOrchestration.OrchestrationStatus.ToString());

                traceActivity?.Dispose();
            }

            this.Logger.SendingOrchestratorResponse(
                name,
                response.InstanceId,
                response.Actions.Count,
                GetActionsListForLogging(response.Actions));

            await this.client.CompleteOrchestratorTaskAsync(response, cancellationToken: cancellationToken);
        }

        async Task OnRunActivityAsync(P.ActivityRequest request, string completionToken, CancellationToken cancellation)
        {
            using Activity? traceActivity = TraceHelper.StartTraceActivityForTaskExecution(request);

            OrchestrationInstance instance = request.OrchestrationInstance.ToCore();
            string rawInput = request.Input;
            int inputSize = rawInput != null ? Encoding.UTF8.GetByteCount(rawInput) : 0;
            this.Logger.ReceivedActivityRequest(request.Name, request.TaskId, instance.InstanceId, inputSize);

            P.TaskFailureDetails? failureDetails = null;
            TaskContext innerContext = new(instance);
            innerContext.ExceptionPropertiesProvider = this.exceptionPropertiesProvider;

            TaskName name = new(request.Name);
            string? output = null;

            failureDetails = EvaluateOrchestrationVersioning(this.worker.workerOptions.Versioning, request.Version, out bool versioningFailed);
            if (!versioningFailed)
            {
                try
                {
                    await using AsyncServiceScope scope = this.worker.services.CreateAsyncScope();
                    if (this.worker.Factory.TryCreateActivity(name, scope.ServiceProvider, out ITaskActivity? activity))
                    {
                        // Both the factory invocation and the RunAsync could involve user code and need to be handled as
                        // part of try/catch.
                        TaskActivity shim = this.shimFactory.CreateActivity(name, activity);
                        output = await shim.RunAsync(innerContext, request.Input);
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
                    failureDetails = applicationException.ToTaskFailureDetails(this.exceptionPropertiesProvider);
                }
            }
            else
            {
                if (this.worker.workerOptions.Versioning?.FailureStrategy == DurableTaskWorkerOptions.VersionFailureStrategy.Reject)
                {
                    this.Logger.AbandoningActivityWorkItem(instance.InstanceId, request.Name, request.TaskId, completionToken);
                    await this.client.AbandonTaskActivityWorkItemAsync(
                        new P.AbandonActivityTaskRequest
                        {
                            CompletionToken = completionToken,
                        },
                        cancellationToken: cancellation);
                }

                return;
            }

            int outputSizeInBytes = 0;
            if (failureDetails != null)
            {
                traceActivity?.SetStatus(ActivityStatusCode.Error, failureDetails.ErrorMessage);

                outputSizeInBytes = failureDetails.GetApproximateByteCount();
            }
            else if (output != null)
            {
                outputSizeInBytes = Encoding.UTF8.GetByteCount(output);
            }

            string successOrFailure = failureDetails != null ? "failure" : "success";
            this.Logger.SendingActivityResponse(
                successOrFailure, name, request.TaskId, instance.InstanceId, outputSizeInBytes);

            P.ActivityResponse response = new()
            {
                InstanceId = instance.InstanceId,
                TaskId = request.TaskId,
                Result = output,
                FailureDetails = failureDetails,
                CompletionToken = completionToken,
            };

            // Stop the trace activity here to avoid including the completion time in the latency calculation
            traceActivity?.Stop();

            await this.client.CompleteActivityTaskAsync(response, cancellationToken: cancellation);
        }

        async Task OnRunEntityBatchAsync(
            EntityBatchRequest batchRequest,
            CancellationToken cancellation,
            string? completionToken = null,
            List<P.OperationInfo>? operationInfos = null)
        {
            var coreEntityId = DTCore.Entities.EntityId.FromString(batchRequest.InstanceId!);
            EntityId entityId = new(coreEntityId.Name, coreEntityId.Key);

            TaskName name = new(entityId.Name);

            EntityBatchResult? batchResult;

            try
            {
                await using AsyncServiceScope scope = this.worker.services.CreateAsyncScope();
                IDurableTaskFactory2 factory = (IDurableTaskFactory2)this.worker.Factory;

                if (factory.TryCreateEntity(name, scope.ServiceProvider, out ITaskEntity? entity))
                {
                    // Both the factory invocation and the RunAsync could involve user code and need to be handled as
                    // part of try/catch.
                    TaskEntity shim = this.shimFactory.CreateEntity(name, entity, entityId);
                    batchResult = await shim.ExecuteOperationBatchAsync(batchRequest);
                }
                else
                {
                    // we could not find the entity. This is considered an application error,
                    // so we return a non-retriable error-OperationResult for each operation in the batch.
                    batchResult = new EntityBatchResult()
                    {
                        Actions = [], // no actions
                        EntityState = batchRequest.EntityState, // state is unmodified
                        Results = Enumerable.Repeat(
                            new OperationResult()
                            {
                                FailureDetails = new FailureDetails(
                                    errorType: "EntityTaskNotFound",
                                    errorMessage: $"No entity task named '{name}' was found.",
                                    stackTrace: null,
                                    innerFailure: null,
                                    isNonRetriable: true),
                            },
                            batchRequest.Operations!.Count).ToList(),
                        FailureDetails = null,
                    };
                }
            }
            catch (Exception frameworkException)
            {
                // return a result with failure details.
                // this will cause the batch to be abandoned and retried
                // (possibly after a delay and on a different worker).
                batchResult = new EntityBatchResult()
                {
                    FailureDetails = new FailureDetails(frameworkException),
                };
            }

            P.EntityBatchResult response = batchResult.ToEntityBatchResult(
                completionToken,
                operationInfos?.Take(batchResult.Results?.Count ?? 0));

            await this.client.CompleteEntityTaskAsync(response, cancellationToken: cancellation);
        }
    }
}
