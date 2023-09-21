// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using DurableTask.Core;
using DurableTask.Core.Entities;
using DurableTask.Core.Entities.OperationFormat;
using DurableTask.Core.History;
using Grpc.Core;
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
    class Processor
    {
        static readonly Google.Protobuf.WellKnownTypes.Empty EmptyMessage = new();

        readonly GrpcDurableTaskWorker worker;
        readonly TaskHubSidecarServiceClient sidecar;
        readonly DurableTaskShimFactory shimFactory;

        public Processor(GrpcDurableTaskWorker worker, TaskHubSidecarServiceClient sidecar)
        {
            this.worker = worker;
            this.sidecar = sidecar;
            this.shimFactory = new DurableTaskShimFactory(this.worker.options, this.worker.loggerFactory);
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

        async Task<AsyncServerStreamingCall<P.WorkItem>> ConnectAsync(CancellationToken cancellation)
        {
            await this.sidecar!.HelloAsync(EmptyMessage, cancellationToken: cancellation);
            this.Logger.EstablishedWorkItemConnection();

            // Get the stream for receiving work-items
            return this.sidecar!.GetWorkItems(new P.GetWorkItemsRequest(), cancellationToken: cancellation);
        }

        async Task ProcessWorkItemsAsync(AsyncServerStreamingCall<P.WorkItem> stream, CancellationToken cancellation)
        {
            while (!cancellation.IsCancellationRequested)
            {
                await foreach (P.WorkItem workItem in stream.ResponseStream.ReadAllAsync(cancellation))
                {
                    if (workItem.RequestCase == P.WorkItem.RequestOneofCase.OrchestratorRequest)
                    {
                        this.RunBackgroundTask(workItem, () => this.OnRunOrchestratorAsync(
                            workItem.OrchestratorRequest));
                    }
                    else if (workItem.RequestCase == P.WorkItem.RequestOneofCase.ActivityRequest)
                    {
                        this.RunBackgroundTask(workItem, () => this.OnRunActivityAsync(workItem.ActivityRequest));
                    }
                    else if (workItem.RequestCase == P.WorkItem.RequestOneofCase.EntityRequest)
                    {
                        this.RunBackgroundTask(workItem, () => this.OnRunEntityBatchAsync(workItem.EntityRequest));
                    }
                    else
                    {
                        this.Logger.UnexpectedWorkItemType(workItem.RequestCase.ToString());
                    }
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

        async Task OnRunOrchestratorAsync(P.OrchestratorRequest request)
        {
            OrchestratorExecutionResult? result = null;
            P.TaskFailureDetails? failureDetails = null;
            TaskName name = new("(unknown)");

            try
            {
                OrchestrationRuntimeState runtimeState = BuildRuntimeState(request);
                name = new(runtimeState.Name);

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
                this.Logger.OrchestratorFailed(name, request.InstanceId, unexpected.ToString());
                failureDetails = unexpected.ToTaskFailureDetails();
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

            this.Logger.SendingOrchestratorResponse(
                name,
                response.InstanceId,
                response.Actions.Count,
                GetActionsListForLogging(response.Actions));

            await this.sidecar.CompleteOrchestratorTaskAsync(response);
        }

        async Task OnRunActivityAsync(P.ActivityRequest request)
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
            };

            await this.sidecar.CompleteActivityTaskAsync(response);
        }

        async Task OnRunEntityBatchAsync(P.EntityBatchRequest request)
        {
            var coreEntityId = DTCore.Entities.EntityId.FromString(request.InstanceId);
            EntityId entityId = new(coreEntityId.Name, coreEntityId.Key);

            TaskName name = new(entityId.Name);

            EntityBatchRequest batchRequest = request.ToEntityBatchRequest();
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
                        Actions = new List<OperationAction>(), // no actions
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
                // return a result with no results, same state,
                // and which contains failure details
                batchResult = new EntityBatchResult()
                {
                    Actions = new List<OperationAction>(),
                    EntityState = batchRequest.EntityState,
                    Results = new List<OperationResult>(),
                    FailureDetails = new FailureDetails(frameworkException),
                };
            }

            // convert the result to protobuf format and send it back
            P.EntityBatchResult response = batchResult.ToEntityBatchResult();
            await this.sidecar.CompleteEntityTaskAsync(response);
        }
    }
}
