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
using System.Diagnostics.CodeAnalysis;
using DurableTask.Core;
using DurableTask.Core.Command;
using DurableTask.Core.History;
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
                    ParentInstance = null /* TODO: need to update protobuf schema */,
                    Correlation = null /* TODO */,
                    ScheduledStartTime = null /* TODO */,
                };
                break;
            ////case P.HistoryEvent.EventTypeOneofCase.ExecutionCompleted:
            ////    break;
            ////case P.HistoryEvent.EventTypeOneofCase.ExecutionFailed:
            ////    break;
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
                    proto.TaskFailed.Reason,
                    proto.TaskFailed.Details);
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
                    proto.SubOrchestrationInstanceFailed.Reason,
                    proto.SubOrchestrationInstanceFailed.Details);
                break;
            case P.HistoryEvent.EventTypeOneofCase.TimerCreated:
                historyEvent = new TimerCreatedEvent(
                    proto.EventId,
                    ConvertTimestamp(proto.TimerCreated.FireAt));
                break;
            case P.HistoryEvent.EventTypeOneofCase.TimerFired:
                historyEvent = new TimerFiredEvent(
                    proto.TimerFired.TimerId,
                    ConvertTimestamp(proto.TimerFired.FireAt));
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
            ////case P.HistoryEvent.EventTypeOneofCase.GenericEvent:
            ////    break;
            ////case P.HistoryEvent.EventTypeOneofCase.HistoryState:
            ////    break;
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
        IEnumerable<OrchestratorAction> actions)
    {
        var response = new P.OrchestratorResponse { InstanceId = instanceId };

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
                    break;
                default:
                    throw new NotImplementedException($"Unknown orchestrator action: {action.OrchestratorActionType}");
            }

            response.Actions.Add(protoAction);
        }

        return response;
    }

    internal static P.ActivityResponse ConstructActivityResponse(
        int taskId,
        string? serializedOutput)
    {
        return new P.ActivityResponse
        {
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
}
