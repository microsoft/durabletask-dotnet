// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using DurableTask.Core;
using DurableTask.Core.Command;
using DurableTask.Core.History;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using P = DurableTask.Protobuf;

namespace DurableTask.Grpc;

static class ProtoUtils
{
    internal static HistoryEvent ConvertHistoryEvent(P.HistoryEvent proto)
    {
        HistoryEvent historyEvent;
        switch (proto.EventTypeCase)
        {
            case P.HistoryEvent.EventTypeOneofCase.ContinueAsNew:
                historyEvent = new ContinueAsNewEvent(proto.EventId, proto.ContinueAsNew.Input);
                break;
            case P.HistoryEvent.EventTypeOneofCase.ExecutionStarted:
                historyEvent = new ExecutionStartedEvent(proto.EventId, proto.ExecutionStarted.Input)
                {
                    Name = proto.ExecutionStarted.Name,
                    Version = proto.ExecutionStarted.Version,
                    OrchestrationInstance = ConvertOrchestrationInstance(proto.ExecutionStarted.OrchestrationInstance),
                    ParentInstance = proto.ExecutionStarted.ParentInstance == null ? null : new ParentInstance
                    {
                        Name = proto.ExecutionStarted.ParentInstance.Name,
                        Version = proto.ExecutionStarted.ParentInstance.Version,
                        OrchestrationInstance = ConvertOrchestrationInstance(proto.ExecutionStarted.ParentInstance.OrchestrationInstance),
                        TaskScheduleId = proto.ExecutionStarted.ParentInstance.TaskScheduledId,
                    },
                    Correlation = proto.ExecutionStarted.CorrelationData,
                    ScheduledStartTime = proto.ExecutionStarted.ScheduledStartTimestamp?.ToDateTime(),
                };
                break;
            case P.HistoryEvent.EventTypeOneofCase.ExecutionCompleted:
                historyEvent = new ExecutionCompletedEvent(
                    proto.EventId,
                    proto.ExecutionCompleted.Result,
                    ConvertOrchestrationRuntimeStatus(proto.ExecutionCompleted.OrchestrationStatus));
                break;
            case P.HistoryEvent.EventTypeOneofCase.ExecutionTerminated:
                historyEvent = new ExecutionTerminatedEvent(proto.EventId, proto.ExecutionTerminated.Input);
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
                    ConvertFailureDetails(proto.TaskFailed.FailureDetails));
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
                    reason: null  /* not supported */,
                    details: null /* not supported */,
                    ConvertFailureDetails(proto.SubOrchestrationInstanceFailed.FailureDetails));
                break;
            case P.HistoryEvent.EventTypeOneofCase.TimerCreated:
                historyEvent = new TimerCreatedEvent(
                    proto.EventId,
                    ConvertTimestamp(proto.TimerCreated.FireAt));
                break;
            case P.HistoryEvent.EventTypeOneofCase.TimerFired:
                historyEvent = new TimerFiredEvent(
                    eventId: -1,
                    ConvertTimestamp(proto.TimerFired.FireAt))
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
                throw new NotImplementedException($"Deserialization of {proto.EventTypeCase} is not implemented.");
        }

        historyEvent.Timestamp = ConvertTimestamp(proto.Timestamp);
        return historyEvent;
    }

    static DateTime ConvertTimestamp(Timestamp ts) => ts.ToDateTime();

    static Timestamp ConvertTimestamp(DateTime dateTime)
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

    internal static P.OrchestratorResponse ConstructOrchestratorResponse(
        string instanceId,
        string? customStatus,
        IEnumerable<OrchestratorAction> actions)
    {
        var response = new P.OrchestratorResponse
        {
            InstanceId = instanceId,
            CustomStatus = customStatus,
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
                        FireAt = ConvertTimestamp(createTimerAction.FireAt),
                    };
                    break;
                case OrchestratorActionType.SendEvent:
                    var sendEventAction = (SendEventOrchestratorAction)action;
                    if (sendEventAction.Instance == null)
                    {
                        throw new ArgumentException($"{nameof(SendEventOrchestratorAction)} cannot have a null Instance property!");
                    }

                    protoAction.SendEvent = new P.SendEventAction
                    {
                        Instance = ConvertOrchestrationInstance(sendEventAction.Instance),
                        Name = sendEventAction.EventName,
                        Data = sendEventAction.EventData,
                    };
                    break;
                case OrchestratorActionType.OrchestrationComplete:
                    var completeAction = (OrchestrationCompleteOrchestratorAction)action;
                    protoAction.CompleteOrchestration = new P.CompleteOrchestrationAction
                    {
                        CarryoverEvents =
                        {
                            // TODO
                        },
                        Details = completeAction.Details,
                        NewVersion = completeAction.NewVersion,
                        OrchestrationStatus = ConvertOrchestrationRuntimeStatus(completeAction.OrchestrationStatus),
                        Result = completeAction.Result,
                    };

                    if (completeAction.OrchestrationStatus == OrchestrationStatus.Failed)
                    {
                        protoAction.CompleteOrchestration.FailureDetails = ConvertFailureDetails(completeAction.FailureDetails);
                    }
                    break;
                default:
                    throw new NotImplementedException($"Unknown orchestrator action: {action.OrchestratorActionType}");
            }

            response.Actions.Add(protoAction);
        }

        return response;
    }

    internal static P.ActivityResponse ConstructActivityResponse(
        string instanceId,
        int taskId,
        string? serializedOutput)
    {
        return new P.ActivityResponse
        {
            InstanceId = instanceId,
            TaskId = taskId,
            Result = serializedOutput,
        };
    }

    static P.OrchestrationStatus ConvertOrchestrationRuntimeStatus(OrchestrationStatus status)
    {
        return(P.OrchestrationStatus)status;
    }

    internal static OrchestrationStatus ConvertOrchestrationRuntimeStatus(P.OrchestrationStatus status)
    {
        return (OrchestrationStatus)status;
    }

    [return: NotNullIfNotNull("instanceProto")]
    internal static OrchestrationInstance? ConvertOrchestrationInstance(P.OrchestrationInstance? instanceProto)
    {
        if (instanceProto == null)
        {
            return null;
        }

        return new OrchestrationInstance
        {
            InstanceId = instanceProto.InstanceId,
            ExecutionId = instanceProto.ExecutionId,
        };
    }

    static P.OrchestrationInstance ConvertOrchestrationInstance(OrchestrationInstance instance)
    {
        return new P.OrchestrationInstance
        {
            InstanceId = instance.InstanceId,
            ExecutionId = instance.ExecutionId,
        };
    }

    static FailureDetails? ConvertFailureDetails(P.TaskFailureDetails? failureDetails)
    {
        if (failureDetails == null)
        {
            return null;
        }

        return new FailureDetails(
            failureDetails.ErrorType,
            failureDetails.ErrorMessage,
            failureDetails.StackTrace,
            ConvertFailureDetails(failureDetails.InnerFailure),
            failureDetails.IsNonRetriable);
    }

    internal static TaskFailureDetails? ConvertTaskFailureDetails(P.TaskFailureDetails? failureDetails)
    {
        if (failureDetails == null)
        {
            return null;
        }

        return new TaskFailureDetails(
            failureDetails.ErrorType,
            failureDetails.ErrorMessage,
            failureDetails.StackTrace,
            ConvertTaskFailureDetails(failureDetails.InnerFailure));
    }

    static P.TaskFailureDetails? ConvertFailureDetails(FailureDetails? failureDetails)
    {
        if (failureDetails == null)
        {
            return null;
        }

        return new P.TaskFailureDetails
        {
            ErrorType = failureDetails.ErrorType ?? "(unkown)",
            ErrorMessage = failureDetails.ErrorMessage ?? "(unkown)",
            StackTrace = failureDetails.StackTrace,
            IsNonRetriable = failureDetails.IsNonRetriable,
            InnerFailure = ConvertFailureDetails(failureDetails.InnerFailure),
        };
    }

    internal static P.TaskFailureDetails? ToTaskFailureDetails(Exception? e)
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
            InnerFailure = ToTaskFailureDetails(e.InnerException),
        };
    }

    internal static int GetApproximateByteCount(P.TaskFailureDetails failureDetails)
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
            byteCount += GetApproximateByteCount(failureDetails.InnerFailure);
        }

        return byteCount;
    }

    internal static T Base64Decode<T>(string encodedMessage, MessageParser parser) where T : IMessage
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
                throw new ArgumentException($"Failed to base64-decode the '{typeof(T).Name}' payload: {status}", nameof(encodedMessage));
            }

            return (T)parser.ParseFrom(buffer, 0, bytesWritten);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
