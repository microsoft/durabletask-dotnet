// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.Command;
using DurableTask.Core.History;
using DurableTask.Core.Query;
using Google.Protobuf.WellKnownTypes;
using Proto = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Sidecar.Grpc;

static class ProtobufUtils
{
    internal static Proto.HistoryEvent ToHistoryEventProto(HistoryEvent e)
    {
        var payload = new Proto.HistoryEvent()
        {
            EventId = e.EventId,
            Timestamp = Timestamp.FromDateTime(e.Timestamp),
        };

        switch (e.EventType)
        {
            case EventType.ContinueAsNew:
                var continueAsNew = (ContinueAsNewEvent)e;
                payload.ContinueAsNew = new Proto.ContinueAsNewEvent
                {
                    Input = continueAsNew.Result,
                };
                break;
            case EventType.EventRaised:
                var eventRaised = (EventRaisedEvent)e;
                payload.EventRaised = new Proto.EventRaisedEvent
                {
                    Name = eventRaised.Name,
                    Input = eventRaised.Input,
                };
                break;
            case EventType.EventSent:
                var eventSent = (EventSentEvent)e;
                payload.EventSent = new Proto.EventSentEvent
                {
                    Name = eventSent.Name,
                    Input = eventSent.Input,
                    InstanceId = eventSent.InstanceId,
                };
                break;
            case EventType.ExecutionCompleted:
                var completedEvent = (ExecutionCompletedEvent)e;
                payload.ExecutionCompleted = new Proto.ExecutionCompletedEvent
                {
                    OrchestrationStatus = Proto.OrchestrationStatus.Completed,
                    Result = completedEvent.Result,
                };
                break;
            case EventType.ExecutionFailed:
                var failedEvent = (ExecutionCompletedEvent)e;
                payload.ExecutionCompleted = new Proto.ExecutionCompletedEvent
                {
                    OrchestrationStatus = Proto.OrchestrationStatus.Failed,
                    Result = failedEvent.Result,
                };
                break;
            case EventType.ExecutionStarted:
                // Start of a new orchestration instance
                var startedEvent = (ExecutionStartedEvent)e;
                payload.ExecutionStarted = new Proto.ExecutionStartedEvent
                {
                    Name = startedEvent.Name,
                    Version = startedEvent.Version,
                    Input = startedEvent.Input,
                    OrchestrationInstance = new Proto.OrchestrationInstance
                    {
                        InstanceId = startedEvent.OrchestrationInstance.InstanceId,
                        ExecutionId = startedEvent.OrchestrationInstance.ExecutionId,
                    },
                    ParentInstance = startedEvent.ParentInstance == null ? null : new Proto.ParentInstanceInfo
                    {
                        Name = startedEvent.ParentInstance.Name,
                        Version = startedEvent.ParentInstance.Version,
                        TaskScheduledId = startedEvent.ParentInstance.TaskScheduleId,
                        OrchestrationInstance = new Proto.OrchestrationInstance
                        {
                            InstanceId = startedEvent.ParentInstance.OrchestrationInstance.InstanceId,
                            ExecutionId = startedEvent.ParentInstance.OrchestrationInstance.ExecutionId,
                        },
                    },
                    ScheduledStartTimestamp = startedEvent.ScheduledStartTime == null ? null : Timestamp.FromDateTime(startedEvent.ScheduledStartTime.Value),
                    ParentTraceContext = startedEvent.ParentTraceContext is null ? null : new Proto.TraceContext
                    {
                        TraceParent = startedEvent.ParentTraceContext.TraceParent,
                        TraceState = startedEvent.ParentTraceContext.TraceState,
                    },
                };
                break;
            case EventType.ExecutionTerminated:
                var terminatedEvent = (ExecutionTerminatedEvent)e;
                payload.ExecutionTerminated = new Proto.ExecutionTerminatedEvent
                {
                    Input = terminatedEvent.Input,
                };
                break;
            case EventType.TaskScheduled:
                var taskScheduledEvent = (TaskScheduledEvent)e;
                payload.TaskScheduled = new Proto.TaskScheduledEvent
                {
                    Name = taskScheduledEvent.Name,
                    Version = taskScheduledEvent.Version,
                    Input = taskScheduledEvent.Input,
                    ParentTraceContext = taskScheduledEvent.ParentTraceContext is null ? null : new Proto.TraceContext
                    {
                        TraceParent = taskScheduledEvent.ParentTraceContext.TraceParent,
                        TraceState = taskScheduledEvent.ParentTraceContext.TraceState,
                    },
                };
                break;
            case EventType.TaskCompleted:
                var taskCompletedEvent = (TaskCompletedEvent)e;
                payload.TaskCompleted = new Proto.TaskCompletedEvent
                {
                    Result = taskCompletedEvent.Result,
                    TaskScheduledId = taskCompletedEvent.TaskScheduledId,
                };
                break;
            case EventType.TaskFailed:
                var taskFailedEvent = (TaskFailedEvent)e;
                payload.TaskFailed = new Proto.TaskFailedEvent
                {
                    FailureDetails = GetFailureDetails(taskFailedEvent.FailureDetails),
                    TaskScheduledId = taskFailedEvent.TaskScheduledId,
                };
                break;
            case EventType.SubOrchestrationInstanceCreated:
                var subOrchestrationCreated = (SubOrchestrationInstanceCreatedEvent)e;
                payload.SubOrchestrationInstanceCreated = new Proto.SubOrchestrationInstanceCreatedEvent
                {
                    Input = subOrchestrationCreated.Input,
                    InstanceId = subOrchestrationCreated.InstanceId,
                    Name = subOrchestrationCreated.Name,
                    Version = subOrchestrationCreated.Version,
                };
                break;
            case EventType.SubOrchestrationInstanceCompleted:
                var subOrchestrationCompleted = (SubOrchestrationInstanceCompletedEvent)e;
                payload.SubOrchestrationInstanceCompleted = new Proto.SubOrchestrationInstanceCompletedEvent
                {
                    Result = subOrchestrationCompleted.Result,
                    TaskScheduledId = subOrchestrationCompleted.TaskScheduledId,
                };
                break;
            case EventType.SubOrchestrationInstanceFailed:
                var subOrchestrationFailed = (SubOrchestrationInstanceFailedEvent)e;
                payload.SubOrchestrationInstanceFailed = new Proto.SubOrchestrationInstanceFailedEvent
                {
                    FailureDetails = GetFailureDetails(subOrchestrationFailed.FailureDetails),
                    TaskScheduledId = subOrchestrationFailed.TaskScheduledId,
                };
                break;
            case EventType.TimerCreated:
                var timerCreatedEvent = (TimerCreatedEvent)e;
                payload.TimerCreated = new Proto.TimerCreatedEvent
                {
                    FireAt = Timestamp.FromDateTime(timerCreatedEvent.FireAt),
                };
                break;
            case EventType.TimerFired:
                var timerFiredEvent = (TimerFiredEvent)e;
                payload.TimerFired = new Proto.TimerFiredEvent
                {
                    FireAt = Timestamp.FromDateTime(timerFiredEvent.FireAt),
                    TimerId = timerFiredEvent.TimerId,
                };
                break;
            case EventType.OrchestratorStarted:
                // This event has no data
                payload.OrchestratorStarted = new Proto.OrchestratorStartedEvent();
                break;
            case EventType.OrchestratorCompleted:
                // This event has no data
                payload.OrchestratorCompleted = new Proto.OrchestratorCompletedEvent();
                break;
            case EventType.GenericEvent:
                var genericEvent = (GenericEvent)e;
                payload.GenericEvent = new Proto.GenericEvent
                {
                    Data = genericEvent.Data,
                };
                break;
            case EventType.HistoryState:
                var historyStateEvent = (HistoryStateEvent)e;
                payload.HistoryState = new Proto.HistoryStateEvent
                {
                    OrchestrationState = new Proto.OrchestrationState
                    {
                        InstanceId = historyStateEvent.State.OrchestrationInstance.InstanceId,
                        Name = historyStateEvent.State.Name,
                        Version = historyStateEvent.State.Version,
                        Input = historyStateEvent.State.Input,
                        Output = historyStateEvent.State.Output,
                        ScheduledStartTimestamp = historyStateEvent.State.ScheduledStartTime == null ? null : Timestamp.FromDateTime(historyStateEvent.State.ScheduledStartTime.Value),
                        CreatedTimestamp = Timestamp.FromDateTime(historyStateEvent.State.CreatedTime),
                        LastUpdatedTimestamp = Timestamp.FromDateTime(historyStateEvent.State.LastUpdatedTime),
                        OrchestrationStatus = (Proto.OrchestrationStatus)historyStateEvent.State.OrchestrationStatus,
                        CustomStatus = historyStateEvent.State.Status,
                    },
                };
                break;
            case EventType.ExecutionSuspended:
                var suspendedEvent = (ExecutionSuspendedEvent)e;
                payload.ExecutionSuspended = new Proto.ExecutionSuspendedEvent
                {
                    Input = suspendedEvent.Reason,
                };
                break;
            case EventType.ExecutionResumed:
                var resumedEvent = (ExecutionResumedEvent)e;
                payload.ExecutionResumed = new Proto.ExecutionResumedEvent
                {
                    Input = resumedEvent.Reason,
                };
                break;
            default:
                throw new NotSupportedException($"Found unsupported history event '{e.EventType}'.");
        }

        return payload;
    }

    internal static OrchestratorAction ToOrchestratorAction(Proto.OrchestratorAction a)
    {
        switch (a.OrchestratorActionTypeCase)
        {
            case Proto.OrchestratorAction.OrchestratorActionTypeOneofCase.ScheduleTask:
                return new ScheduleTaskOrchestratorAction
                {
                    Id = a.Id,
                    Input = a.ScheduleTask.Input,
                    Name = a.ScheduleTask.Name,
                    Version = a.ScheduleTask.Version,
                };
            case Proto.OrchestratorAction.OrchestratorActionTypeOneofCase.CreateSubOrchestration:
                return new CreateSubOrchestrationAction
                {
                    Id = a.Id,
                    Input = a.CreateSubOrchestration.Input,
                    Name = a.CreateSubOrchestration.Name,
                    InstanceId = a.CreateSubOrchestration.InstanceId,
                    Tags = null, // TODO
                    Version = a.CreateSubOrchestration.Version,
                };
            case Proto.OrchestratorAction.OrchestratorActionTypeOneofCase.CreateTimer:
                return new CreateTimerOrchestratorAction
                {
                    Id = a.Id,
                    FireAt = a.CreateTimer.FireAt.ToDateTime(),
                };
            case Proto.OrchestratorAction.OrchestratorActionTypeOneofCase.SendEvent:
                return new SendEventOrchestratorAction
                {
                    Id = a.Id,
                    Instance = new OrchestrationInstance
                    {
                        InstanceId = a.SendEvent.Instance.InstanceId,
                        ExecutionId = a.SendEvent.Instance.ExecutionId,
                    },
                    EventName = a.SendEvent.Name,
                    EventData = a.SendEvent.Data,
                };
            case Proto.OrchestratorAction.OrchestratorActionTypeOneofCase.CompleteOrchestration:
                var completedAction = a.CompleteOrchestration;
                var action = new OrchestrationCompleteOrchestratorAction
                {
                    Id = a.Id,
                    OrchestrationStatus = (OrchestrationStatus)completedAction.OrchestrationStatus,
                    Result = completedAction.Result,
                    Details = completedAction.Details,
                    FailureDetails = GetFailureDetails(completedAction.FailureDetails),
                    NewVersion = completedAction.NewVersion,
                };

                if (completedAction.CarryoverEvents?.Count > 0)
                {
                    foreach (var e in completedAction.CarryoverEvents)
                    {
                        // Only raised events are supported for carryover
                        if (e.EventRaised is Proto.EventRaisedEvent eventRaised)
                        {
                            action.CarryoverEvents.Add(new EventRaisedEvent(e.EventId, eventRaised.Input)
                            {
                                Name = eventRaised.Name,
                            });
                        }
                        
                    }
                }

                return action;
            default:
                throw new NotSupportedException($"Received unsupported action type '{a.OrchestratorActionTypeCase}'.");
        }
    }

    internal static FailureDetails? GetFailureDetails(Proto.TaskFailureDetails? failureDetails)
    {
        if (failureDetails == null)
        {
            return null;
        }

        return new FailureDetails(
            failureDetails.ErrorType,
            failureDetails.ErrorMessage,
            failureDetails.StackTrace,
            GetFailureDetails(failureDetails.InnerFailure),
            failureDetails.IsNonRetriable);
    }

    internal static Proto.TaskFailureDetails? GetFailureDetails(FailureDetails? failureDetails)
    {
        if (failureDetails == null)
        {
            return null;
        }

        return new Proto.TaskFailureDetails
        {
            ErrorType = failureDetails.ErrorType,
            ErrorMessage = failureDetails.ErrorMessage,
            StackTrace = failureDetails.StackTrace,
            InnerFailure = GetFailureDetails(failureDetails.InnerFailure),
            IsNonRetriable = failureDetails.IsNonRetriable,
        };
    }

    internal static OrchestrationQuery ToOrchestrationQuery(Proto.QueryInstancesRequest request)
    {
        var query = new OrchestrationQuery()
        {
            RuntimeStatus = request.Query.RuntimeStatus?.Select(status => (OrchestrationStatus)status).ToList(),
            CreatedTimeFrom = request.Query.CreatedTimeFrom?.ToDateTime(),
            CreatedTimeTo = request.Query.CreatedTimeTo?.ToDateTime(),
            TaskHubNames = request.Query.TaskHubNames,
            PageSize = request.Query.MaxInstanceCount,
            ContinuationToken = request.Query.ContinuationToken,
            InstanceIdPrefix = request.Query.InstanceIdPrefix,
            FetchInputsAndOutputs = request.Query.FetchInputsAndOutputs,
        };

        return query;
    }

    internal static Proto.QueryInstancesResponse CreateQueryInstancesResponse(OrchestrationQueryResult result, Proto.QueryInstancesRequest request)
    {
        Proto.QueryInstancesResponse response = new Proto.QueryInstancesResponse
        {
            ContinuationToken = result.ContinuationToken
        };
        foreach (OrchestrationState state in result.OrchestrationState)
        {
            var orchestrationState = new Proto.OrchestrationState
            {
                InstanceId = state.OrchestrationInstance.InstanceId,
                Name = state.Name,
                Version = state.Version,
                Input = state.Input,
                Output = state.Output,
                ScheduledStartTimestamp = state.ScheduledStartTime == null ? null : Timestamp.FromDateTime(state.ScheduledStartTime.Value),
                CreatedTimestamp = Timestamp.FromDateTime(state.CreatedTime),
                LastUpdatedTimestamp = Timestamp.FromDateTime(state.LastUpdatedTime),
                OrchestrationStatus = (Proto.OrchestrationStatus)state.OrchestrationStatus,
                CustomStatus = state.Status,
            };
            response.OrchestrationState.Add(orchestrationState);
        }
        return response;
    }

    internal static PurgeInstanceFilter ToPurgeInstanceFilter(Proto.PurgeInstancesRequest request)
    {
        var purgeInstanceFilter = new PurgeInstanceFilter(
            request.PurgeInstanceFilter.CreatedTimeFrom.ToDateTime(),
            request.PurgeInstanceFilter.CreatedTimeTo?.ToDateTime(),
            request.PurgeInstanceFilter.RuntimeStatus?.Select(status => (OrchestrationStatus)status).ToList()
        );
        return purgeInstanceFilter;
    }

    internal static Proto.PurgeInstancesResponse CreatePurgeInstancesResponse(PurgeResult result)
    {
        Proto.PurgeInstancesResponse response = new Proto.PurgeInstancesResponse
        {
            DeletedInstanceCount = result.DeletedInstanceCount
        };
        return response;
    }
}
