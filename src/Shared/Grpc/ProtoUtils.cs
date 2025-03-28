// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using DurableTask.Core;
using DurableTask.Core.Command;
using DurableTask.Core.Entities;
using DurableTask.Core.Entities.OperationFormat;
using DurableTask.Core.History;
using DurableTask.Core.Tracing;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using DTCore = DurableTask.Core;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask;

/// <summary>
/// Protobuf utilities and helpers.
/// </summary>
static class ProtoUtils
{
    /// <summary>
    /// Converts a history event from <see cref="P.HistoryEvent" /> to <see cref="HistoryEvent" />.
    /// </summary>
    /// <param name="proto">The proto history event to converter.</param>
    /// <returns>The converted history event.</returns>
    /// <exception cref="NotSupportedException">When the provided history event type is not supported.</exception>
    internal static HistoryEvent ConvertHistoryEvent(P.HistoryEvent proto)
    {
        return ConvertHistoryEvent(proto, conversionState: null);
    }

    /// <summary>
    /// Converts a history event from <see cref="P.HistoryEvent" /> to <see cref="HistoryEvent"/>, and performs
    /// stateful conversions of entity-related events.
    /// </summary>
    /// <param name="proto">The proto history event to converter.</param>
    /// <param name="conversionState">State needed for converting entity-related history entries and actions.</param>
    /// <returns>The converted history event.</returns>
    /// <exception cref="NotSupportedException">When the provided history event type is not supported.</exception>
    internal static HistoryEvent ConvertHistoryEvent(P.HistoryEvent proto, EntityConversionState? conversionState)
    {
        Check.NotNull(proto);
        HistoryEvent historyEvent;
        switch (proto.EventTypeCase)
        {
            case P.HistoryEvent.EventTypeOneofCase.ContinueAsNew:
                historyEvent = new ContinueAsNewEvent(proto.EventId, proto.ContinueAsNew.Input);
                break;
            case P.HistoryEvent.EventTypeOneofCase.ExecutionStarted:
                OrchestrationInstance instance = proto.ExecutionStarted.OrchestrationInstance.ToCore();
                conversionState?.SetOrchestrationInstance(instance);
                historyEvent = new ExecutionStartedEvent(proto.EventId, proto.ExecutionStarted.Input)
                {
                    Name = proto.ExecutionStarted.Name,
                    Version = proto.ExecutionStarted.Version,
                    OrchestrationInstance = instance,
                    ParentInstance = proto.ExecutionStarted.ParentInstance == null ? null : new ParentInstance
                    {
                        Name = proto.ExecutionStarted.ParentInstance.Name,
                        Version = proto.ExecutionStarted.ParentInstance.Version,
                        OrchestrationInstance = proto.ExecutionStarted.ParentInstance.OrchestrationInstance.ToCore(),
                        TaskScheduleId = proto.ExecutionStarted.ParentInstance.TaskScheduledId,
                    },
                    ScheduledStartTime = proto.ExecutionStarted.ScheduledStartTimestamp?.ToDateTime(),
                };
                break;
            case P.HistoryEvent.EventTypeOneofCase.ExecutionCompleted:
                historyEvent = new ExecutionCompletedEvent(
                    proto.EventId,
                    proto.ExecutionCompleted.Result,
                    proto.ExecutionCompleted.OrchestrationStatus.ToCore());
                break;
            case P.HistoryEvent.EventTypeOneofCase.ExecutionTerminated:
                historyEvent = new ExecutionTerminatedEvent(proto.EventId, proto.ExecutionTerminated.Input);
                break;
            case P.HistoryEvent.EventTypeOneofCase.ExecutionSuspended:
                historyEvent = new ExecutionSuspendedEvent(proto.EventId, proto.ExecutionSuspended.Input);
                break;
            case P.HistoryEvent.EventTypeOneofCase.ExecutionResumed:
                historyEvent = new ExecutionResumedEvent(proto.EventId, proto.ExecutionResumed.Input);
                break;
            case P.HistoryEvent.EventTypeOneofCase.TaskScheduled:
                historyEvent = new TaskScheduledEvent(
                    proto.EventId,
                    proto.TaskScheduled.Name,
                    proto.TaskScheduled.Version,
                    proto.TaskScheduled.Input);
                break;
            case P.HistoryEvent.EventTypeOneofCase.TaskCompleted:
                historyEvent = new TaskCompletedEvent(
                    proto.EventId,
                    proto.TaskCompleted.TaskScheduledId,
                    proto.TaskCompleted.Result);
                break;
            case P.HistoryEvent.EventTypeOneofCase.TaskFailed:
                historyEvent = new TaskFailedEvent(
                    proto.EventId,
                    proto.TaskFailed.TaskScheduledId,
                    reason: null,  /* not supported */
                    details: null, /* not supported */
                    proto.TaskFailed.FailureDetails.ToCore());
                break;
            case P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCreated:
                historyEvent = new SubOrchestrationInstanceCreatedEvent(proto.EventId)
                {
                    Input = proto.SubOrchestrationInstanceCreated.Input,
                    InstanceId = proto.SubOrchestrationInstanceCreated.InstanceId,
                    Name = proto.SubOrchestrationInstanceCreated.Name,
                    Version = proto.SubOrchestrationInstanceCreated.Version,
                };
                break;
            case P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCompleted:
                historyEvent = new SubOrchestrationInstanceCompletedEvent(
                    proto.EventId,
                    proto.SubOrchestrationInstanceCompleted.TaskScheduledId,
                    proto.SubOrchestrationInstanceCompleted.Result);
                break;
            case P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceFailed:
                historyEvent = new SubOrchestrationInstanceFailedEvent(
                    proto.EventId,
                    proto.SubOrchestrationInstanceFailed.TaskScheduledId,
                    reason: null /* not supported */,
                    details: null /* not supported */,
                    proto.SubOrchestrationInstanceFailed.FailureDetails.ToCore());
                break;
            case P.HistoryEvent.EventTypeOneofCase.TimerCreated:
                historyEvent = new TimerCreatedEvent(
                    proto.EventId,
                    proto.TimerCreated.FireAt.ToDateTime());
                break;
            case P.HistoryEvent.EventTypeOneofCase.TimerFired:
                historyEvent = new TimerFiredEvent(
                    eventId: -1,
                    proto.TimerFired.FireAt.ToDateTime())
                {
                    TimerId = proto.TimerFired.TimerId,
                };
                break;
            case P.HistoryEvent.EventTypeOneofCase.OrchestratorStarted:
                historyEvent = new OrchestratorStartedEvent(proto.EventId);
                break;
            case P.HistoryEvent.EventTypeOneofCase.OrchestratorCompleted:
                historyEvent = new OrchestratorCompletedEvent(proto.EventId);
                break;
            case P.HistoryEvent.EventTypeOneofCase.EventSent:
                historyEvent = new EventSentEvent(proto.EventId)
                {
                    InstanceId = proto.EventSent.InstanceId,
                    Name = proto.EventSent.Name,
                    Input = proto.EventSent.Input,
                };
                break;
            case P.HistoryEvent.EventTypeOneofCase.EventRaised:
                historyEvent = new EventRaisedEvent(proto.EventId, proto.EventRaised.Input)
                {
                    Name = proto.EventRaised.Name,
                };
                break;
            case P.HistoryEvent.EventTypeOneofCase.EntityOperationCalled:
                historyEvent = EntityConversions.EncodeOperationCalled(proto, conversionState!.CurrentInstance);
                conversionState?.EntityRequestIds.Add(proto.EntityOperationCalled.RequestId);
                break;
            case P.HistoryEvent.EventTypeOneofCase.EntityOperationSignaled:
                historyEvent = EntityConversions.EncodeOperationSignaled(proto);
                conversionState?.EntityRequestIds.Add(proto.EntityOperationSignaled.RequestId);
                break;
            case P.HistoryEvent.EventTypeOneofCase.EntityLockRequested:
                historyEvent = EntityConversions.EncodeLockRequested(proto, conversionState!.CurrentInstance);
                conversionState?.AddUnlockObligations(proto.EntityLockRequested);
                break;
            case P.HistoryEvent.EventTypeOneofCase.EntityUnlockSent:
                historyEvent = EntityConversions.EncodeUnlockSent(proto, conversionState!.CurrentInstance);
                conversionState?.RemoveUnlockObligation(proto.EntityUnlockSent.TargetInstanceId);
                break;
            case P.HistoryEvent.EventTypeOneofCase.EntityLockGranted:
                historyEvent = EntityConversions.EncodeLockGranted(proto);
                break;
            case P.HistoryEvent.EventTypeOneofCase.EntityOperationCompleted:
                historyEvent = EntityConversions.EncodeOperationCompleted(proto);
                break;
            case P.HistoryEvent.EventTypeOneofCase.EntityOperationFailed:
                historyEvent = EntityConversions.EncodeOperationFailed(proto);
                break;
            case P.HistoryEvent.EventTypeOneofCase.GenericEvent:
                historyEvent = new GenericEvent(proto.EventId, proto.GenericEvent.Data);
                break;
            case P.HistoryEvent.EventTypeOneofCase.HistoryState:
                historyEvent = new HistoryStateEvent(
                    proto.EventId,
                    new OrchestrationState
                    {
                        OrchestrationInstance = new OrchestrationInstance
                        {
                            InstanceId = proto.HistoryState.OrchestrationState.InstanceId,
                        },
                        Name = proto.HistoryState.OrchestrationState.Name,
                        Version = proto.HistoryState.OrchestrationState.Version,
                        ScheduledStartTime = proto.HistoryState.OrchestrationState.ScheduledStartTimestamp.ToDateTime(),
                        CreatedTime = proto.HistoryState.OrchestrationState.CreatedTimestamp.ToDateTime(),
                        LastUpdatedTime = proto.HistoryState.OrchestrationState.LastUpdatedTimestamp.ToDateTime(),
                        Input = proto.HistoryState.OrchestrationState.Input,
                        Output = proto.HistoryState.OrchestrationState.Output,
                        Status = proto.HistoryState.OrchestrationState.CustomStatus,
                    });
                break;
            default:
                throw new NotSupportedException($"Deserialization of {proto.EventTypeCase} is not supported.");
        }

        historyEvent.Timestamp = proto.Timestamp.ToDateTime();
        return historyEvent;
    }

    /// <summary>
    /// Converts a <see cref="DateTime" /> to a gRPC <see cref="Timestamp" />.
    /// </summary>
    /// <param name="dateTime">The date-time to convert.</param>
    /// <returns>The gRPC timestamp.</returns>
    internal static Timestamp ToTimestamp(this DateTime dateTime)
    {
        // The protobuf libraries require timestamps to be in UTC
        if (dateTime.Kind == DateTimeKind.Unspecified)
        {
            dateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
        }
        else if (dateTime.Kind == DateTimeKind.Local)
        {
            dateTime = dateTime.ToUniversalTime();
        }

        return Timestamp.FromDateTime(dateTime);
    }

    /// <summary>
    /// Converts a <see cref="DateTime" /> to a gRPC <see cref="Timestamp" />.
    /// </summary>
    /// <param name="dateTime">The date-time to convert.</param>
    /// <returns>The gRPC timestamp.</returns>
    internal static Timestamp? ToTimestamp(this DateTime? dateTime)
        => dateTime.HasValue ? dateTime.Value.ToTimestamp() : null;

    /// <summary>
    /// Converts a <see cref="DateTimeOffset" /> to a gRPC <see cref="Timestamp" />.
    /// </summary>
    /// <param name="dateTime">The date-time to convert.</param>
    /// <returns>The gRPC timestamp.</returns>
    internal static Timestamp ToTimestamp(this DateTimeOffset dateTime) => Timestamp.FromDateTimeOffset(dateTime);

    /// <summary>
    /// Converts a <see cref="DateTimeOffset" /> to a gRPC <see cref="Timestamp" />.
    /// </summary>
    /// <param name="dateTime">The date-time to convert.</param>
    /// <returns>The gRPC timestamp.</returns>
    internal static Timestamp? ToTimestamp(this DateTimeOffset? dateTime)
        => dateTime.HasValue ? dateTime.Value.ToTimestamp() : null;

    /// <summary>
    /// Constructs a <see cref="P.OrchestratorResponse" />.
    /// </summary>
    /// <param name="instanceId">The orchestrator instance ID.</param>
    /// <param name="customStatus">The orchestrator customer status or <c>null</c> if no custom status.</param>
    /// <param name="actions">The orchestrator actions.</param>
    /// <param name="completionToken">
    /// The completion token for the work item. It must be the exact same <see cref="P.WorkItem.CompletionToken" />
    /// value that was provided by the corresponding <see cref="P.WorkItem"/> that triggered the orchestrator execution.
    /// </param>
    /// <param name="entityConversionState">The entity conversion state, or null if no conversion is required.</param>
    /// <returns>The orchestrator response.</returns>
    /// <exception cref="NotSupportedException">When an orchestrator action is unknown.</exception>
    internal static P.OrchestratorResponse ConstructOrchestratorResponse(
        string instanceId,
        string? customStatus,
        IEnumerable<OrchestratorAction> actions,
        string completionToken,
        EntityConversionState? entityConversionState)
    {
        Check.NotNull(actions);
        var response = new P.OrchestratorResponse
        {
            InstanceId = instanceId,
            CustomStatus = customStatus,
            CompletionToken = completionToken,
        };

        foreach (OrchestratorAction action in actions)
        {
            var protoAction = new P.OrchestratorAction { Id = action.Id };

            switch (action.OrchestratorActionType)
            {
                case OrchestratorActionType.ScheduleOrchestrator:
                    var scheduleTaskAction = (ScheduleTaskOrchestratorAction)action;
                    protoAction.ScheduleTask = new P.ScheduleTaskAction
                    {
                        Name = scheduleTaskAction.Name,
                        Version = scheduleTaskAction.Version,
                        Input = scheduleTaskAction.Input,
                    };
                    break;
                case OrchestratorActionType.CreateSubOrchestration:
                    var subOrchestrationAction = (CreateSubOrchestrationAction)action;
                    protoAction.CreateSubOrchestration = new P.CreateSubOrchestrationAction
                    {
                        Input = subOrchestrationAction.Input,
                        InstanceId = subOrchestrationAction.InstanceId,
                        Name = subOrchestrationAction.Name,
                        Version = subOrchestrationAction.Version,
                    };
                    break;
                case OrchestratorActionType.CreateTimer:
                    var createTimerAction = (CreateTimerOrchestratorAction)action;
                    protoAction.CreateTimer = new P.CreateTimerAction
                    {
                        FireAt = createTimerAction.FireAt.ToTimestamp(),
                    };
                    break;
                case OrchestratorActionType.SendEvent:
                    var sendEventAction = (SendEventOrchestratorAction)action;
                    if (sendEventAction.Instance == null)
                    {
                        throw new ArgumentException(
                            $"{nameof(SendEventOrchestratorAction)} cannot have a null Instance property!");
                    }

                    if (entityConversionState is not null
                        && DTCore.Common.Entities.IsEntityInstance(sendEventAction.Instance.InstanceId)
                        && sendEventAction.EventName is not null
                        && sendEventAction.EventData is not null)
                    {
                        P.SendEntityMessageAction sendAction = new P.SendEntityMessageAction();
                        protoAction.SendEntityMessage = sendAction;

                        EntityConversions.DecodeEntityMessageAction(
                            sendEventAction.EventName,
                            sendEventAction.EventData,
                            sendEventAction.Instance.InstanceId,
                            sendAction,
                            out string requestId);

                        entityConversionState.EntityRequestIds.Add(requestId);

                        switch (sendAction.EntityMessageTypeCase)
                        {
                            case P.SendEntityMessageAction.EntityMessageTypeOneofCase.EntityLockRequested:
                                entityConversionState.AddUnlockObligations(sendAction.EntityLockRequested);
                                break;
                            case P.SendEntityMessageAction.EntityMessageTypeOneofCase.EntityUnlockSent:
                                entityConversionState.RemoveUnlockObligation(sendAction.EntityUnlockSent.TargetInstanceId);
                                break;
                            default:
                                break;
                        }
                    }
                    else
                    {
                        protoAction.SendEvent = new P.SendEventAction
                        {
                            Instance = sendEventAction.Instance.ToProtobuf(),
                            Name = sendEventAction.EventName,
                            Data = sendEventAction.EventData,
                        };
                    }

                    break;
                case OrchestratorActionType.OrchestrationComplete:

                    if (entityConversionState is not null)
                    {
                        // as a precaution, unlock any entities that were not unlocked for some reason, before
                        // completing the orchestration.
                        foreach ((string target, string criticalSectionId) in entityConversionState.ResetObligations())
                        {
                            response.Actions.Add(new P.OrchestratorAction
                            {
                                Id = action.Id,
                                SendEntityMessage = new P.SendEntityMessageAction
                                {
                                    EntityUnlockSent = new P.EntityUnlockSentEvent
                                    {
                                        CriticalSectionId = criticalSectionId,
                                        TargetInstanceId = target,
                                        ParentInstanceId = entityConversionState.CurrentInstance?.InstanceId,
                                    },
                                },
                            });
                        }
                    }

                    var completeAction = (OrchestrationCompleteOrchestratorAction)action;
                    protoAction.CompleteOrchestration = new P.CompleteOrchestrationAction
                    {
                        CarryoverEvents =
                        {
                            // TODO
                        },
                        Details = completeAction.Details,
                        NewVersion = completeAction.NewVersion,
                        OrchestrationStatus = completeAction.OrchestrationStatus.ToProtobuf(),
                        Result = completeAction.Result,
                    };

                    if (completeAction.OrchestrationStatus == OrchestrationStatus.Failed)
                    {
                        protoAction.CompleteOrchestration.FailureDetails = completeAction.FailureDetails.ToProtobuf();
                    }

                    break;
                default:
                    throw new NotSupportedException($"Unknown orchestrator action: {action.OrchestratorActionType}");
            }

            response.Actions.Add(protoAction);
        }

        return response;
    }

    /// <summary>
    /// Converts a <see cref="P.OrchestrationStatus" /> to a <see cref="OrchestrationStatus" />.
    /// </summary>
    /// <param name="status">The status to convert.</param>
    /// <returns>The converted status.</returns>
    internal static OrchestrationStatus ToCore(this P.OrchestrationStatus status)
    {
        return (OrchestrationStatus)status;
    }

    /// <summary>
    /// Converts a <see cref="P.OrchestrationStatus" /> to a <see cref="OrchestrationStatus" />.
    /// </summary>
    /// <param name="status">The status to convert.</param>
    /// <returns>The converted status.</returns>
    [return: NotNullIfNotNull(nameof(status))]
    internal static OrchestrationInstance? ToCore(this P.OrchestrationInstance? status)
    {
        if (status == null)
        {
            return null;
        }

        return new OrchestrationInstance
        {
            InstanceId = status.InstanceId,
            ExecutionId = status.ExecutionId,
        };
    }

    /// <summary>
    /// Converts a <see cref="P.TaskFailureDetails" /> to a <see cref="TaskFailureDetails" />.
    /// </summary>
    /// <param name="failureDetails">The failure details to convert.</param>
    /// <returns>The converted failure details.</returns>
    [return: NotNullIfNotNull(nameof(failureDetails))]
    internal static TaskFailureDetails? ToTaskFailureDetails(this P.TaskFailureDetails? failureDetails)
    {
        if (failureDetails == null)
        {
            return null;
        }

        return new TaskFailureDetails(
            failureDetails.ErrorType,
            failureDetails.ErrorMessage,
            failureDetails.StackTrace,
            failureDetails.InnerFailure.ToTaskFailureDetails());
    }

    /// <summary>
    /// Converts a <see cref="Exception" /> to <see cref="P.TaskFailureDetails" />.
    /// </summary>
    /// <param name="e">The exception to convert.</param>
    /// <returns>The task failure details.</returns>
    [return: NotNullIfNotNull(nameof(e))]
    internal static P.TaskFailureDetails? ToTaskFailureDetails(this Exception? e)
    {
        if (e == null)
        {
            return null;
        }

        return new P.TaskFailureDetails
        {
            ErrorType = e.GetType().FullName,
            ErrorMessage = e.Message,
            StackTrace = e.StackTrace,
            InnerFailure = e.InnerException.ToTaskFailureDetails(),
        };
    }

    /// <summary>
    /// Converts a <see cref="P.EntityBatchRequest" /> to a <see cref="EntityBatchRequest" />.
    /// </summary>
    /// <param name="entityBatchRequest">The entity batch request to convert.</param>
    /// <returns>The converted entity batch request.</returns>
    [return: NotNullIfNotNull(nameof(entityBatchRequest))]
    internal static EntityBatchRequest? ToEntityBatchRequest(this P.EntityBatchRequest? entityBatchRequest)
    {
        if (entityBatchRequest == null)
        {
            return null;
        }

        return new EntityBatchRequest()
        {
            EntityState = entityBatchRequest.EntityState,
            InstanceId = entityBatchRequest.InstanceId,
            Operations = entityBatchRequest.Operations.Select(r => r.ToOperationRequest()).ToList(),
        };
    }

    /// <summary>
    /// Converts a <see cref="P.EntityRequest" /> to a <see cref="EntityBatchRequest" />.
    /// </summary>
    /// <param name="entityRequest">The entity request to convert.</param>
    /// <param name="batchRequest">The converted request.</param>
    /// <param name="operationInfos">Additional info about each operation, required by DTS.</param>
    internal static void ToEntityBatchRequest(
        this P.EntityRequest entityRequest,
        out EntityBatchRequest batchRequest,
        out List<P.OperationInfo> operationInfos)
    {
        batchRequest = new EntityBatchRequest()
        {
            EntityState = entityRequest.EntityState,
            InstanceId = entityRequest.InstanceId,
            Operations = [], // operations are added to this collection below
        };

        operationInfos = new(entityRequest.OperationRequests.Count);

        foreach (P.HistoryEvent? op in entityRequest.OperationRequests)
        {
            if (op.EntityOperationSignaled is not null)
            {
                batchRequest.Operations.Add(new OperationRequest
                {
                    Id = Guid.Parse(op.EntityOperationSignaled.RequestId),
                    Operation = op.EntityOperationSignaled.Operation,
                    Input = op.EntityOperationSignaled.Input,
                });
                operationInfos.Add(new P.OperationInfo
                {
                    RequestId = op.EntityOperationSignaled.RequestId,
                    ResponseDestination = null, // means we don't send back a response to the caller
                });
            }
            else if (op.EntityOperationCalled is not null)
            {
                batchRequest.Operations.Add(new OperationRequest
                {
                    Id = Guid.Parse(op.EntityOperationCalled.RequestId),
                    Operation = op.EntityOperationCalled.Operation,
                    Input = op.EntityOperationCalled.Input,
                });
                operationInfos.Add(new P.OperationInfo
                {
                    RequestId = op.EntityOperationCalled.RequestId,
                    ResponseDestination = new P.OrchestrationInstance
                    {
                        InstanceId = op.EntityOperationCalled.ParentInstanceId,
                        ExecutionId = op.EntityOperationCalled.ParentExecutionId,
                    },
                });
            }
        }
    }

    /// <summary>
    /// Converts a <see cref="P.OperationRequest" /> to a <see cref="OperationRequest" />.
    /// </summary>
    /// <param name="operationRequest">The operation request to convert.</param>
    /// <returns>The converted operation request.</returns>
    [return: NotNullIfNotNull(nameof(operationRequest))]
    internal static OperationRequest? ToOperationRequest(this P.OperationRequest? operationRequest)
    {
        if (operationRequest == null)
        {
            return null;
        }

        return new OperationRequest()
        {
            Operation = operationRequest.Operation,
            Input = operationRequest.Input,
            Id = Guid.Parse(operationRequest.RequestId),
            TraceContext = operationRequest.TraceContext != null ?
            new DistributedTraceContext(
                operationRequest.TraceContext.TraceParent,
                operationRequest.TraceContext.TraceState) : null,
        };
    }

    /// <summary>
    /// Converts a <see cref="P.OperationResult" /> to a <see cref="OperationResult" />.
    /// </summary>
    /// <param name="operationResult">The operation result to convert.</param>
    /// <returns>The converted operation result.</returns>
    [return: NotNullIfNotNull(nameof(operationResult))]
    internal static OperationResult? ToOperationResult(this P.OperationResult? operationResult)
    {
        if (operationResult == null)
        {
            return null;
        }

        switch (operationResult.ResultTypeCase)
        {
            case P.OperationResult.ResultTypeOneofCase.Success:
                return new OperationResult()
                {
                    Result = operationResult.Success.Result,
                    EndTime = operationResult.Success.EndTime?.ToDateTime(),
                };

            case P.OperationResult.ResultTypeOneofCase.Failure:
                return new OperationResult()
                {
                    FailureDetails = operationResult.Failure.FailureDetails.ToCore(),
                    EndTime = operationResult.Failure.EndTime?.ToDateTime(),
                };

            default:
                throw new NotSupportedException($"Deserialization of {operationResult.ResultTypeCase} is not supported.");
        }
    }

    /// <summary>
    /// Converts a <see cref="OperationResult" /> to <see cref="P.OperationResult" />.
    /// </summary>
    /// <param name="operationResult">The operation result to convert.</param>
    /// <returns>The converted operation result.</returns>
    [return: NotNullIfNotNull(nameof(operationResult))]
    internal static P.OperationResult? ToOperationResult(this OperationResult? operationResult)
    {
        if (operationResult == null)
        {
            return null;
        }

        if (operationResult.FailureDetails == null)
        {
            return new P.OperationResult()
            {
                Success = new P.OperationResultSuccess()
                {
                    Result = operationResult.Result,
                    EndTime = operationResult.EndTime?.ToTimestamp(),
                },
            };
        }
        else
        {
            return new P.OperationResult()
            {
                Failure = new P.OperationResultFailure()
                {
                    FailureDetails = ToProtobuf(operationResult.FailureDetails),
                    EndTime = operationResult.EndTime?.ToTimestamp(),
                },
            };
        }
    }

    /// <summary>
    /// Converts a <see cref="P.OperationAction" /> to a <see cref="OperationAction" />.
    /// </summary>
    /// <param name="operationAction">The operation action to convert.</param>
    /// <returns>The converted operation action.</returns>
    [return: NotNullIfNotNull(nameof(operationAction))]
    internal static OperationAction? ToOperationAction(this P.OperationAction? operationAction)
    {
        if (operationAction == null)
        {
            return null;
        }

        switch (operationAction.OperationActionTypeCase)
        {
            case P.OperationAction.OperationActionTypeOneofCase.SendSignal:

                return new SendSignalOperationAction()
                {
                    Name = operationAction.SendSignal.Name,
                    Input = operationAction.SendSignal.Input,
                    InstanceId = operationAction.SendSignal.InstanceId,
                    ScheduledTime = operationAction.SendSignal.ScheduledTime?.ToDateTime(),
                    RequestTime = operationAction.SendSignal.RequestTime?.ToDateTime(),
                    ParentTraceContext = operationAction.SendSignal.ParentTraceContext != null ?
                        new DistributedTraceContext(
                            operationAction.SendSignal.ParentTraceContext.TraceParent,
                            operationAction.SendSignal.ParentTraceContext.TraceState) : null,
                };

            case P.OperationAction.OperationActionTypeOneofCase.StartNewOrchestration:

                return new StartNewOrchestrationOperationAction()
                {
                    Name = operationAction.StartNewOrchestration.Name,
                    Input = operationAction.StartNewOrchestration.Input,
                    InstanceId = operationAction.StartNewOrchestration.InstanceId,
                    Version = operationAction.StartNewOrchestration.Version,
                    ScheduledStartTime = operationAction.StartNewOrchestration.ScheduledTime?.ToDateTime(),
                    RequestTime = operationAction.StartNewOrchestration.RequestTime?.ToDateTime(),
                    ParentTraceContext = operationAction.StartNewOrchestration.ParentTraceContext != null ?
                        new DistributedTraceContext(
                            operationAction.StartNewOrchestration.ParentTraceContext.TraceParent,
                            operationAction.StartNewOrchestration.ParentTraceContext.TraceState) : null,
                };
            default:
                throw new NotSupportedException($"Deserialization of {operationAction.OperationActionTypeCase} is not supported.");
        }
    }

    /// <summary>
    /// Converts a <see cref="OperationAction" /> to <see cref="P.OperationAction" />.
    /// </summary>
    /// <param name="operationAction">The operation action to convert.</param>
    /// <returns>The converted operation action.</returns>
    [return: NotNullIfNotNull(nameof(operationAction))]
    internal static P.OperationAction? ToOperationAction(this OperationAction? operationAction)
    {
        if (operationAction == null)
        {
            return null;
        }

        var action = new P.OperationAction();

        switch (operationAction)
        {
            case SendSignalOperationAction sendSignalAction:

                action.SendSignal = new P.SendSignalAction()
                {
                    Name = sendSignalAction.Name,
                    Input = sendSignalAction.Input,
                    InstanceId = sendSignalAction.InstanceId,
                    ScheduledTime = sendSignalAction.ScheduledTime?.ToTimestamp(),
                    RequestTime = sendSignalAction.RequestTime?.ToTimestamp(),
                    ParentTraceContext = sendSignalAction.ParentTraceContext != null ?
                        new P.TraceContext
                        {
                            TraceParent = sendSignalAction.ParentTraceContext.TraceParent,
                            TraceState = sendSignalAction.ParentTraceContext.TraceState,
                        }
                    : null,
                };
                break;

            case StartNewOrchestrationOperationAction startNewOrchestrationAction:

                action.StartNewOrchestration = new P.StartNewOrchestrationAction()
                {
                    Name = startNewOrchestrationAction.Name,
                    Input = startNewOrchestrationAction.Input,
                    Version = startNewOrchestrationAction.Version,
                    InstanceId = startNewOrchestrationAction.InstanceId,
                    ScheduledTime = startNewOrchestrationAction.ScheduledStartTime?.ToTimestamp(),
                    RequestTime = startNewOrchestrationAction.RequestTime?.ToTimestamp(),
                    ParentTraceContext = startNewOrchestrationAction.ParentTraceContext != null ?
                        new P.TraceContext
                        {
                            TraceParent = startNewOrchestrationAction.ParentTraceContext.TraceParent,
                            TraceState = startNewOrchestrationAction.ParentTraceContext.TraceState,
                        }
                    : null,
                };
                break;
        }

        return action;
    }

    /// <summary>
    /// Converts a <see cref="P.EntityBatchResult" /> to a <see cref="EntityBatchResult" />.
    /// </summary>
    /// <param name="entityBatchResult">The operation result to convert.</param>
    /// <returns>The converted operation result.</returns>
    [return: NotNullIfNotNull(nameof(entityBatchResult))]
    internal static EntityBatchResult? ToEntityBatchResult(this P.EntityBatchResult? entityBatchResult)
    {
        if (entityBatchResult == null)
        {
            return null;
        }

        return new EntityBatchResult()
        {
            Actions = entityBatchResult.Actions.Select(operationAction => operationAction!.ToOperationAction()).ToList(),
            EntityState = entityBatchResult.EntityState,
            Results = entityBatchResult.Results.Select(operationResult => operationResult!.ToOperationResult()).ToList(),
            FailureDetails = entityBatchResult.FailureDetails.ToCore(),
        };
    }

    /// <summary>
    /// Converts a <see cref="EntityBatchResult" /> to <see cref="P.EntityBatchResult" />.
    /// </summary>
    /// <param name="entityBatchResult">The operation result to convert.</param>
    /// <param name="completionToken">The completion token, or null for the older protocol.</param>
    /// <param name="operationInfos">Additional information about each operation, required by DTS.</param>
    /// <returns>The converted operation result.</returns>
    [return: NotNullIfNotNull(nameof(entityBatchResult))]
    internal static P.EntityBatchResult? ToEntityBatchResult(
        this EntityBatchResult? entityBatchResult,
        string? completionToken = null,
        IEnumerable<P.OperationInfo>? operationInfos = null)
    {
        if (entityBatchResult == null)
        {
            return null;
        }

        return new P.EntityBatchResult()
        {
            EntityState = entityBatchResult.EntityState,
            FailureDetails = entityBatchResult.FailureDetails.ToProtobuf(),
            Actions = { entityBatchResult.Actions?.Select(a => a.ToOperationAction()) ?? [] },
            Results = { entityBatchResult.Results?.Select(a => a.ToOperationResult()) ?? [] },
            CompletionToken = completionToken ?? string.Empty,
            OperationInfos = { operationInfos ?? [] },
        };
    }

    /// <summary>
    /// Converts the gRPC representation of orchestrator entity parameters to the DT.Core representation.
    /// </summary>
    /// <param name="parameters">The DT.Core representation.</param>
    /// <returns>The gRPC representation.</returns>
    [return: NotNullIfNotNull(nameof(parameters))]
    internal static TaskOrchestrationEntityParameters? ToCore(this P.OrchestratorEntityParameters? parameters)
    {
        if (parameters == null)
        {
            return null;
        }

        return new TaskOrchestrationEntityParameters()
        {
            EntityMessageReorderWindow = parameters.EntityMessageReorderWindow.ToTimeSpan(),
        };
    }

    /// <summary>
    /// Gets the approximate byte count for a <see cref="P.TaskFailureDetails" />.
    /// </summary>
    /// <param name="failureDetails">The failure details.</param>
    /// <returns>The approximate byte count.</returns>
    internal static int GetApproximateByteCount(this P.TaskFailureDetails failureDetails)
    {
        // Protobuf strings are always UTF-8: https://developers.google.com/protocol-buffers/docs/proto3#scalar
        Encoding encoding = Encoding.UTF8;

        int byteCount = 0;
        if (failureDetails.ErrorType != null)
        {
            byteCount += encoding.GetByteCount(failureDetails.ErrorType);
        }

        if (failureDetails.ErrorMessage != null)
        {
            byteCount += encoding.GetByteCount(failureDetails.ErrorMessage);
        }

        if (failureDetails.StackTrace != null)
        {
            byteCount += encoding.GetByteCount(failureDetails.StackTrace);
        }

        if (failureDetails.InnerFailure != null)
        {
            byteCount += failureDetails.InnerFailure.GetApproximateByteCount();
        }

        return byteCount;
    }

    /// <summary>
    /// Decode a protobuf message from a base64 string.
    /// </summary>
    /// <typeparam name="T">The type to decode to.</typeparam>
    /// <param name="parser">The message parser.</param>
    /// <param name="encodedMessage">The base64 encoded message.</param>
    /// <returns>The decoded message.</returns>
    /// <exception cref="ArgumentException">If decoding fails.</exception>
    internal static T Base64Decode<T>(this MessageParser parser, string encodedMessage) where T : IMessage
    {
        // Decode the base64 in a way that doesn't allocate a byte[] on each request
        int encodedByteCount = Encoding.UTF8.GetByteCount(encodedMessage);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(encodedByteCount);
        try
        {
            // The Base64 APIs require first converting the string into UTF-8 bytes. We then
            // do an in-place conversion from base64 UTF-8 bytes to protobuf bytes so that
            // we can finally decode the protobuf request.
            Encoding.UTF8.GetBytes(encodedMessage, 0, encodedMessage.Length, buffer, 0);
            OperationStatus status = Base64.DecodeFromUtf8InPlace(
                buffer.AsSpan(0, encodedByteCount),
                out int bytesWritten);
            if (status != OperationStatus.Done)
            {
                throw new ArgumentException(
                    $"Failed to base64-decode the '{typeof(T).Name}' payload: {status}", nameof(encodedMessage));
            }

            return (T)parser.ParseFrom(buffer, 0, bytesWritten);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Converts a grpc <see cref="P.TaskFailureDetails" /> to a <see cref="FailureDetails" />.
    /// </summary>
    /// <param name="failureDetails">The failure details to convert.</param>
    /// <returns>The converted failure details.</returns>
    internal static FailureDetails? ToCore(this P.TaskFailureDetails? failureDetails)
    {
        if (failureDetails == null)
        {
            return null;
        }

        return new FailureDetails(
            failureDetails.ErrorType,
            failureDetails.ErrorMessage,
            failureDetails.StackTrace,
            failureDetails.InnerFailure.ToCore(),
            failureDetails.IsNonRetriable);
    }

    /// <summary>
    /// Converts a <see cref="FailureDetails" /> to a grpc <see cref="P.TaskFailureDetails" />.
    /// </summary>
    /// <param name="failureDetails">The failure details to convert.</param>
    /// <returns>The converted failure details.</returns>
    static P.TaskFailureDetails? ToProtobuf(this FailureDetails? failureDetails)
    {
        if (failureDetails == null)
        {
            return null;
        }

        return new P.TaskFailureDetails
        {
            ErrorType = failureDetails.ErrorType ?? "(unknown)",
            ErrorMessage = failureDetails.ErrorMessage ?? "(unknown)",
            StackTrace = failureDetails.StackTrace,
            IsNonRetriable = failureDetails.IsNonRetriable,
            InnerFailure = failureDetails.InnerFailure.ToProtobuf(),
        };
    }

    static P.OrchestrationStatus ToProtobuf(this OrchestrationStatus status)
    {
        return (P.OrchestrationStatus)status;
    }

    static P.OrchestrationInstance ToProtobuf(this OrchestrationInstance instance)
    {
        return new P.OrchestrationInstance
        {
            InstanceId = instance.InstanceId,
            ExecutionId = instance.ExecutionId,
        };
    }

    /// <summary>
    /// Tracks state required for converting orchestration histories containing entity-related events.
    /// </summary>
    internal class EntityConversionState
    {
        readonly bool insertMissingEntityUnlocks;

        OrchestrationInstance? instance;
        HashSet<string>? entityRequestIds;
        Dictionary<string, string>? unlockObligations;

        /// <summary>
        /// Initializes a new instance of the <see cref="EntityConversionState"/> class.
        /// </summary>
        /// <param name="insertMissingEntityUnlocks">Whether to insert missing unlock events in to the history
        /// when the orchestration completes.</param>
        public EntityConversionState(bool insertMissingEntityUnlocks)
        {
            this.ConvertFromProto = (P.HistoryEvent e) => ProtoUtils.ConvertHistoryEvent(e, this);
            this.insertMissingEntityUnlocks = insertMissingEntityUnlocks;
        }

        /// <summary>
        /// Gets a function that converts a history event in protobuf format to a core history event.
        /// </summary>
        public Func<P.HistoryEvent, HistoryEvent> ConvertFromProto { get; }

        /// <summary>
        /// Gets the orchestration instance of this history.
        /// </summary>
        public OrchestrationInstance? CurrentInstance => this.instance;

        /// <summary>
        /// Gets the set of guids that have been used as entity request ids in this history.
        /// </summary>
        public HashSet<string> EntityRequestIds => this.entityRequestIds ??= new();

        /// <summary>
        /// Records the orchestration instance, which may be needed for some conversions.
        /// </summary>
        /// <param name="instance">The orchestration instance.</param>
        public void SetOrchestrationInstance(OrchestrationInstance instance)
        {
            this.instance = instance;
        }

        /// <summary>
        /// Adds unlock obligations for all entities that are being locked by this request.
        /// </summary>
        /// <param name="request">The lock request.</param>
        public void AddUnlockObligations(P.EntityLockRequestedEvent request)
        {
            if (!this.insertMissingEntityUnlocks)
            {
                return;
            }

            this.unlockObligations ??= new();

            foreach (string target in request.LockSet)
            {
                this.unlockObligations[target] = request.CriticalSectionId;
            }
        }

        /// <summary>
        /// Removes an unlock obligation.
        /// </summary>
        /// <param name="target">The target entity.</param>
        public void RemoveUnlockObligation(string target)
        {
            if (!this.insertMissingEntityUnlocks)
            {
                return;
            }

            this.unlockObligations?.Remove(target);
        }

        /// <summary>
        /// Returns the remaining unlock obligations, and clears the list.
        /// </summary>
        /// <returns>The unlock obligations.</returns>
        public IEnumerable<(string Target, string CriticalSectionId)> ResetObligations()
        {
            if (!this.insertMissingEntityUnlocks)
            {
                yield break;
            }

            if (this.unlockObligations is not null)
            {
                foreach (var kvp in this.unlockObligations)
                {
                    yield return (kvp.Key, kvp.Value);
                }

                this.unlockObligations = null;
            }
        }
    }
}
