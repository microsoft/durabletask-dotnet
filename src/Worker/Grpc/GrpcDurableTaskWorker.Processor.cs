// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text;
using DurableTask.Core;
using DurableTask.Core.Entities;
using DurableTask.Core.Entities.OperationFormat;
using DurableTask.Core.History;
using Google.Protobuf.Collections;
using Microsoft.DurableTask.Abstractions;
using Microsoft.DurableTask.Entities;
using Microsoft.DurableTask.Worker.Shims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static Microsoft.DurableTask.Protobuf.TaskHubSidecarService;
using DTCore = DurableTask.Core;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc;

/// <summary>
/// The gRPC Durable Task worker.
/// </summary>
sealed partial class GrpcDurableTaskWorker
{
    class Processor : IDisposable
    {
        static readonly Google.Protobuf.WellKnownTypes.Empty EmptyMessage = new();

        readonly GrpcDurableTaskWorker worker;
        readonly TaskHubSidecarServiceClient client;
        readonly DurableTaskShimFactory shimFactory;
        readonly GrpcDurableTaskWorkerOptions.InternalOptions internalOptions;
        readonly LRUCache<string, IEnumerable<HistoryEvent>> orchestrationHistories;
        readonly LRUCache<string, string> entityStates;

        public Processor(GrpcDurableTaskWorker worker, TaskHubSidecarServiceClient client)
        {
            this.worker = worker;
            this.client = client;
            this.shimFactory = new DurableTaskShimFactory(this.worker.grpcOptions, this.worker.loggerFactory);
            this.internalOptions = this.worker.grpcOptions.Internal;
            this.orchestrationHistories = new LRUCache<string, IEnumerable<HistoryEvent>>(
                worker.workerOptions.OrchestrationHistoryCacheSizeInBytes,
                worker.workerOptions.OrchestrationHistoryCacheCheckForStaleItemsPeriodInMilliseconds,
                worker.workerOptions.OrchestrationHistoryCacheStaleEvictionTimeInMilliseconds);
            this.entityStates = new LRUCache<string, string>(
                worker.workerOptions.OrchestrationHistoryCacheSizeInBytes,
                worker.workerOptions.OrchestrationHistoryCacheCheckForStaleItemsPeriodInMilliseconds,
                worker.workerOptions.OrchestrationHistoryCacheStaleEvictionTimeInMilliseconds);
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

        public void Dispose()
        {
            this.orchestrationHistories.Dispose();
            this.entityStates.Dispose();

            GC.SuppressFinalize(this);
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

        static int CalculateSizeInBytes(RepeatedField<P.HistoryEvent> historyEvents)
        {
            int sizeInBytes = 0;
            foreach (P.HistoryEvent historyEvent in historyEvents)
            {
                sizeInBytes += historyEvent.CalculateSize();
            }

            return sizeInBytes;
        }

        static bool IsOrchestrationFinished(OrchestrationStatus orchestrationStatus)
        {
            return orchestrationStatus is OrchestrationStatus.Completed
                or OrchestrationStatus.Terminated
                or OrchestrationStatus.Failed
                or OrchestrationStatus.Canceled;
        }

        async ValueTask<OrchestrationRuntimeState> BuildRuntimeStateAsync(
            P.OrchestratorRequest orchestratorRequest,
            ProtoUtils.EntityConversionState? entityConversionState,
            CancellationToken cancellation)
        {
            Func<P.HistoryEvent, HistoryEvent> converter = entityConversionState is null
                ? ProtoUtils.ConvertHistoryEvent
                : entityConversionState.ConvertFromProto;

            IEnumerable<HistoryEvent>? pastEvents = [];

            if (orchestratorRequest.PastEvents.Count > 0)
            {
                // The history was already provided in the work item request
                pastEvents = orchestratorRequest.PastEvents.Select(converter);
                int sizeInBytes = CalculateSizeInBytes(orchestratorRequest.PastEvents);

                // If the backend provided a history, even if we already have one cached, we should always store it
                this.orchestrationHistories.Put(orchestratorRequest.InstanceId, pastEvents, sizeInBytes);
            }

            // Either the request requires history streaming, or a history exists and none was provided in the request and nothing is cached so we have to stream it
            else if (orchestratorRequest.RequiresHistoryStreaming || (orchestratorRequest.HistoryExists && !this.orchestrationHistories.TryGetValue(orchestratorRequest.InstanceId, out pastEvents)))
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

                pastEvents = [];
                int sizeInBytes = 0;
                await foreach (P.HistoryChunk chunk in streamResponse.ResponseStream.ReadAllAsync(cancellation))
                {
                    pastEvents = pastEvents.Concat(chunk.Events.Select(converter));
                    sizeInBytes += CalculateSizeInBytes(chunk.Events);
                }

                this.orchestrationHistories.Put(orchestratorRequest.InstanceId, pastEvents, sizeInBytes);
            }
            else
            {
                // Even if there is no history, we still want to add an item to the cache. This is necessary because upon the completion of the work item, the new history 
                // will only be added if an entry already exists for this instance id in the cache.
                this.orchestrationHistories.Put(orchestratorRequest.InstanceId, [], 0);
            }

            IEnumerable<HistoryEvent> newEvents = orchestratorRequest.NewEvents.Select(converter);

            // Reconstruct the orchestration state in a way that correctly distinguishes new events from past events
            var runtimeState = new OrchestrationRuntimeState(pastEvents!.ToList());
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
                                cancellation));
                    }
                    else if (workItem.RequestCase == P.WorkItem.RequestOneofCase.ActivityRequest)
                    {
                        this.RunBackgroundTask(
                            workItem,
                            () => this.OnRunActivityAsync(
                                workItem.ActivityRequest,
                                workItem.CompletionToken,
                                cancellation));
                    }
                    else if (workItem.RequestCase == P.WorkItem.RequestOneofCase.EntityRequest)
                    {
                        // Note that in this case, we call run entity batch without setting whether or not the entity state exists, which is by default false.
                        // This is the desired behavior since this type of entity request does not have worker state caching implemented, and so if an entity state is not provided
                        // in the request, that means that no state exists.
                        this.RunBackgroundTask(
                            workItem,
                            () => this.OnRunEntityBatchAsync(workItem.EntityRequest.ToEntityBatchRequest(), cancellation));
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
                                operationInfos,
                                workItem.EntityRequestV2.EntityStateExists));
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

        void RunBackgroundTask(P.WorkItem? workItem, Func<Task> handler)
        {
            // TODO: is Task.Run appropriate here? Should we have finer control over the tasks and their threads?
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
                catch (Exception ex)
                {
                    string instanceId =
                        workItem?.OrchestratorRequest?.InstanceId ??
                        workItem?.ActivityRequest?.OrchestrationInstance?.InstanceId ??
                        string.Empty;
                    this.Logger.UnexpectedError(ex, instanceId);
                }
            });
        }

        async Task OnRunOrchestratorAsync(
            P.OrchestratorRequest request,
            string completionToken,
            CancellationToken cancellationToken)
        {
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
            }
            catch (Exception unexpected)
            {
                // This is not expected: Normally TaskOrchestrationExecutor handles exceptions in user code.
                this.Logger.OrchestratorFailed(name, request.InstanceId, unexpected.ToString());
                failureDetails = unexpected.ToTaskFailureDetails();
            }

            P.OrchestratorResponse response;
            OrchestrationStatus orchestrationStatus;
            if (result != null)
            {
                response = ProtoUtils.ConstructOrchestratorResponse(
                    request.InstanceId,
                    result.CustomStatus,
                    result.Actions,
                    completionToken,
                    entityConversionState,
                    out orchestrationStatus,
                    historyCached: this.orchestrationHistories.ContainsKey(request.InstanceId)); // It could be that the history was never cached if it was "too big" (see comment for "Put" method)
            }
            else if (versioning != null && failureDetails != null && versionFailure)
            {
                this.Logger.OrchestrationVersionFailure(versioning.FailureStrategy.ToString(), failureDetails.ErrorMessage);
                if (versioning.FailureStrategy == DurableTaskWorkerOptions.VersionFailureStrategy.Fail)
                {
                    orchestrationStatus = OrchestrationStatus.Failed;
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
                orchestrationStatus = OrchestrationStatus.Failed;
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

            this.Logger.SendingOrchestratorResponse(
                name,
                response.InstanceId,
                response.Actions.Count,
                GetActionsListForLogging(response.Actions));

            Func<P.HistoryEvent, HistoryEvent> converter = entityConversionState is null
                ? ProtoUtils.ConvertHistoryEvent
                : entityConversionState.ConvertFromProto;

            try
            {
                var completeOrchestratorTaskResponse = await this.client.CompleteOrchestratorTaskAsync(response, cancellationToken: cancellationToken);
                if (!IsOrchestrationFinished(orchestrationStatus))
                {
                    // If the entry has since been evicted from the cache, we do not want to store the new history for it since it will potentially be incomplete if the entry had an existing history attached.
                    if (this.orchestrationHistories.TryGetValueWithSize(request.InstanceId, out (IEnumerable<HistoryEvent>? Value, int Size) pastEventsWithSize))
                    {
                        int sizeInBytes = CalculateSizeInBytes(completeOrchestratorTaskResponse.NewHistory) + pastEventsWithSize.Size;
                        this.orchestrationHistories.Put(
                            request.InstanceId,
                            pastEventsWithSize.Value!.Concat(completeOrchestratorTaskResponse.NewHistory.Select(converter)),
                            sizeInBytes);
                    }
                }
                else
                {
                    this.orchestrationHistories.Remove(request.InstanceId);
                }
            }
            catch (RpcException e) when (e.StatusCode == StatusCode.NotFound)
            {
                // The instance was not found - this can happen if another worker completed the item due to a network failure, for example
                // In this case, we want to remove the orchestration history from the cache since we are no longer responsible for this instance ID
                this.orchestrationHistories.Remove(request.InstanceId);
            }
        }

        async Task OnRunActivityAsync(P.ActivityRequest request, string completionToken, CancellationToken cancellation)
        {
            OrchestrationInstance instance = request.OrchestrationInstance.ToCore();
            string rawInput = request.Input;

            int inputSize = rawInput != null ? Encoding.UTF8.GetByteCount(rawInput) : 0;
            this.Logger.ReceivedActivityRequest(request.Name, request.TaskId, instance.InstanceId, inputSize);

            TaskContext innerContext = new(instance);
            TaskName name = new(request.Name);
            string? output = null;
            P.TaskFailureDetails? failureDetails = null;
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
                failureDetails = applicationException.ToTaskFailureDetails();
            }

            int outputSizeInBytes = 0;
            if (failureDetails != null)
            {
                outputSizeInBytes = failureDetails.GetApproximateByteCount();
            }
            else if (output != null)
            {
                outputSizeInBytes = output.Length * sizeof(char);
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

            await this.client.CompleteActivityTaskAsync(response, cancellationToken: cancellation);
        }

        async Task OnRunEntityBatchAsync(
            EntityBatchRequest batchRequest,
            CancellationToken cancellation,
            string? completionToken = null,
            List<P.OperationInfo>? operationInfos = null,
            bool entityStateExists = false)
        {
            var coreEntityId = DTCore.Entities.EntityId.FromString(batchRequest.InstanceId!);
            EntityId entityId = new(coreEntityId.Name, coreEntityId.Key);

            TaskName name = new(entityId.Name);

            EntityBatchResult? batchResult;
            bool entityStateCached = false;

            try
            {
                await using AsyncServiceScope scope = this.worker.services.CreateAsyncScope();
                IDurableTaskFactory2 factory = (IDurableTaskFactory2)this.worker.Factory;

                if (factory.TryCreateEntity(name, scope.ServiceProvider, out ITaskEntity? entity))
                {
                    // Both the factory invocation and the RunAsync could involve user code and need to be handled as
                    // part of try/catch.
                    TaskEntity shim = this.shimFactory.CreateEntity(name, entity, entityId);

                    string? entityState = null;

                    // If the backend provided an entity state, even if we already have one cached, we should always use it
                    if (batchRequest.EntityState == null)
                    {
                        if (entityStateExists && !this.entityStates.TryGetValue(batchRequest.InstanceId!, out entityState))
                        {
                            P.GetEntityResponse getEntityResponse = await this.client.GetEntityAsync(
                                new P.GetEntityRequest
                                {
                                    InstanceId = batchRequest.InstanceId,
                                    IncludeState = true,
                                },
                                cancellationToken: cancellation);

                            // It should never be possible that this is false if entityStateExists is true, but we check it just in case.
                            if (getEntityResponse.Exists)
                            {
                                batchRequest.EntityState = getEntityResponse.Entity.SerializedState;
                            }
                        }
                        else
                        {
                            // In this case, either we successfully extracted the state from the cache, or a state does not exist, meaning the field should be null.
                            batchRequest.EntityState = entityState;
                        }
                    }

                    batchResult = await shim.ExecuteOperationBatchAsync(batchRequest);

                    // Even if the entity state is the same, we want to issue a put to refresh its position in the cache.
                    if (batchResult.EntityState != null)
                    {
                        entityStateCached = this.entityStates.Put(batchRequest.InstanceId!, batchResult.EntityState, batchResult.EntityState.Length * sizeof(char));
                    }
                    else
                    {
                        // If the entity state is now null, remove whatever state we had in the cache for it, if any.
                        this.entityStates.Remove(batchRequest.InstanceId!);
                    }
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
                    this.entityStates.Remove(batchRequest.InstanceId!);
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
                operationInfos?.Take(batchResult.Results?.Count ?? 0),
                entityStateCached);

            await this.client.CompleteEntityTaskAsync(response, cancellationToken: cancellation);
        }
    }
}
