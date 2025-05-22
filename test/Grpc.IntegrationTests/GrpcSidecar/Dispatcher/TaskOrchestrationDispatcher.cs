// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using DurableTask.Core;
using DurableTask.Core.Command;
using DurableTask.Core.History;
using Microsoft.Extensions.Logging;

namespace Dapr.DurableTask.Sidecar.Dispatcher;

class TaskOrchestrationDispatcher : WorkItemDispatcher<TaskOrchestrationWorkItem>
{
    readonly ILogger log;
    readonly IOrchestrationService service;
    readonly ITaskExecutor taskExecutor;

    public TaskOrchestrationDispatcher(ILogger log, ITrafficSignal trafficSignal, IOrchestrationService service, ITaskExecutor taskExecutor)
        : base(log, trafficSignal)
    {
        this.log = log;
        this.service = service;
        this.taskExecutor = taskExecutor;
    }

    public override int MaxWorkItems => this.service.MaxConcurrentTaskOrchestrationWorkItems;

    public override Task AbandonWorkItemAsync(TaskOrchestrationWorkItem workItem) =>
        this.service.AbandonTaskOrchestrationWorkItemAsync(workItem);

    public override Task<TaskOrchestrationWorkItem?> FetchWorkItemAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        this.service.LockNextTaskOrchestrationWorkItemAsync(timeout, cancellationToken);

    protected override async Task ExecuteWorkItemAsync(TaskOrchestrationWorkItem workItem)
    {
        // Convert the new messages into new history events
        workItem.OrchestrationRuntimeState.AddEvent(new OrchestratorStartedEvent(-1));
        foreach (TaskMessage message in FilterAndSortMessages(workItem))
        {
            workItem.OrchestrationRuntimeState.AddEvent(message.Event);
        }

        OrchestrationInstance? instance = workItem.OrchestrationRuntimeState.OrchestrationInstance;
        if (string.IsNullOrEmpty(instance?.InstanceId))
        {
            throw new ArgumentException($"Could not find an orchestration instance ID in the work item's runtime state.", nameof(workItem));
        }

        // We loop for as long as the orchestrator does a ContinueAsNew
        while (true)
        {
            if (this.log.IsEnabled(LogLevel.Debug))
            {
                IList<HistoryEvent> newEvents = workItem.OrchestrationRuntimeState.NewEvents;
                string newEventSummary = GetEventSummaryForLogging(newEvents);
                this.log.OrchestratorExecuting(
                    workItem.InstanceId,
                    workItem.OrchestrationRuntimeState.Name,
                    newEvents.Count,
                    newEventSummary);
            }

            // Execute the orchestrator code and get back a set of new actions to take.
            // IMPORTANT: This IEnumerable<OrchestratorAction> may be lazily evaluated and should only be enumerated once!
            OrchestratorExecutionResult result = await this.taskExecutor.ExecuteOrchestrator(
                instance,
                workItem.OrchestrationRuntimeState.PastEvents,
                workItem.OrchestrationRuntimeState.NewEvents);

            // Convert the actions into history events and messages.
            // If the actions result in a continue-as-new state, 
            this.ApplyOrchestratorActions(
                result,
                ref workItem.OrchestrationRuntimeState,
                out IList<TaskMessage> activityMessages,
                out IList<TaskMessage> orchestratorMessages,
                out IList<TaskMessage> timerMessages,
                out OrchestrationState? updatedStatus,
                out bool continueAsNew);
            if (continueAsNew)
            {
                // Continue running the orchestration with a new history.
                // Renew the lock if we're getting close to its expiration.
                if (workItem.LockedUntilUtc != default && DateTime.UtcNow.AddMinutes(1) > workItem.LockedUntilUtc)
                {
                    await this.service.RenewTaskOrchestrationWorkItemLockAsync(workItem);
                }

                continue;
            }

            // Commit the changes to the durable store
            await this.service.CompleteTaskOrchestrationWorkItemAsync(
                workItem,
                workItem.OrchestrationRuntimeState,
                activityMessages,
                orchestratorMessages,
                timerMessages,
                continuedAsNewMessage: null /* not supported */,
                updatedStatus);

            break;
        }
    }

    static string GetEventSummaryForLogging(IList<HistoryEvent> actions)
    {
        if (actions.Count == 0)
        {
            return string.Empty;
        }
        else if (actions.Count == 1)
        {
            return actions[0].EventType.ToString();
        }
        else
        {
            // Returns something like "TaskCompleted x5, TimerFired x1,..."
            return string.Join(", ", actions
                .GroupBy(a => a.EventType)
                .Select(group => $"{group.Key} x{group.Count()}"));
        }
    }

    static IEnumerable<TaskMessage> FilterAndSortMessages(TaskOrchestrationWorkItem workItem)
    {
        // Group messages by their instance ID
        static string GetGroupingKey(TaskMessage msg) => msg.OrchestrationInstance.InstanceId;

        // Within a group, put messages with a non-null execution ID first
        static int GetSortOrderWithinGroup(TaskMessage msg)
        {
            if (msg.Event.EventType == EventType.ExecutionStarted)
            {
                // Prioritize ExecutionStarted messages
                return 0;
            }
            else if (msg.OrchestrationInstance.ExecutionId != null)
            {
                // Prioritize messages with an execution ID
                return 1;
            }
            else
            {
                return 2;
            }
        }

        string? executionId = workItem.OrchestrationRuntimeState?.OrchestrationInstance?.ExecutionId;

        foreach (var group in workItem.NewMessages.GroupBy(GetGroupingKey))
        {
            // TODO: Filter out invalid messages (wrong execution ID, duplicate start/complete messages, etc.)
            foreach (TaskMessage msg in group.OrderBy(GetSortOrderWithinGroup))
            {
                yield return msg;
            }
        }
    }

    void ApplyOrchestratorActions(
        OrchestratorExecutionResult result,
        ref OrchestrationRuntimeState runtimeState,
        out IList<TaskMessage> activityMessages,
        out IList<TaskMessage> orchestratorMessages,
        out IList<TaskMessage> timerMessages,
        out OrchestrationState? updatedStatus,
        out bool continueAsNew)
    {
        if (string.IsNullOrEmpty(runtimeState.OrchestrationInstance?.InstanceId))
        {
            throw new ArgumentException($"The provided {nameof(OrchestrationRuntimeState)} doesn't contain an instance ID!", nameof(runtimeState));
        }

        IList<TaskMessage>? newActivityMessages = null;
        IList<TaskMessage>? newTimerMessages = null;
        IList<TaskMessage>? newOrchestratorMessages = null;
        FailureDetails? failureDetails = null;
        continueAsNew = false;

        runtimeState.Status = result.CustomStatus;

        foreach (OrchestratorAction action in result.Actions)
        {
            // TODO: Determine how to handle remaining actions if the instance completed with ContinueAsNew.
            // TODO: Validate each of these actions to make sure they have the appropriate data.
            if (action is ScheduleTaskOrchestratorAction scheduleTaskAction)
            {
                if (string.IsNullOrEmpty(scheduleTaskAction.Name))
                {
                    throw new ArgumentException($"The provided {nameof(ScheduleTaskOrchestratorAction)} has no Name property specified!", nameof(result));
                }

                TaskScheduledEvent scheduledEvent = new(
                    scheduleTaskAction.Id,
                    scheduleTaskAction.Name,
                    scheduleTaskAction.Version,
                    scheduleTaskAction.Input);

                newActivityMessages ??= new List<TaskMessage>();
                newActivityMessages.Add(new TaskMessage
                {
                    Event = scheduledEvent,
                    OrchestrationInstance = runtimeState.OrchestrationInstance,
                });

                runtimeState.AddEvent(scheduledEvent);
            }
            else if (action is CreateTimerOrchestratorAction timerAction)
            {
                TimerCreatedEvent timerEvent = new(timerAction.Id, timerAction.FireAt);

                newTimerMessages ??= new List<TaskMessage>();
                newTimerMessages.Add(new TaskMessage
                {
                    Event = new TimerFiredEvent(-1, timerAction.FireAt)
                    {
                        TimerId = timerAction.Id,
                    },
                    OrchestrationInstance = runtimeState.OrchestrationInstance,
                });

                runtimeState.AddEvent(timerEvent);
            }
            else if (action is CreateSubOrchestrationAction subOrchestrationAction)
            {
                runtimeState.AddEvent(new SubOrchestrationInstanceCreatedEvent(subOrchestrationAction.Id)
                {
                    Name = subOrchestrationAction.Name,
                    Version = subOrchestrationAction.Version,
                    InstanceId = subOrchestrationAction.InstanceId,
                    Input = subOrchestrationAction.Input,
                });

                ExecutionStartedEvent startedEvent = new(-1, subOrchestrationAction.Input)
                {
                    Name = subOrchestrationAction.Name,
                    Version = subOrchestrationAction.Version,
                    OrchestrationInstance = new OrchestrationInstance
                    {
                        InstanceId = subOrchestrationAction.InstanceId,
                        ExecutionId = Guid.NewGuid().ToString("N"),
                    },
                    ParentInstance = new ParentInstance
                    {
                        OrchestrationInstance = runtimeState.OrchestrationInstance,
                        Name = runtimeState.Name,
                        Version = runtimeState.Version,
                        TaskScheduleId = subOrchestrationAction.Id,
                    },
                    Tags = subOrchestrationAction.Tags,
                };

                newOrchestratorMessages ??= new List<TaskMessage>();
                newOrchestratorMessages.Add(new TaskMessage
                {
                    Event = startedEvent,
                    OrchestrationInstance = startedEvent.OrchestrationInstance,
                });
            }
            else if (action is SendEventOrchestratorAction sendEventAction)
            {
                if (string.IsNullOrEmpty(sendEventAction.Instance?.InstanceId))
                {
                    throw new ArgumentException($"The provided {nameof(SendEventOrchestratorAction)} doesn't contain an instance ID!");
                }

                EventSentEvent sendEvent = new(sendEventAction.Id)
                {
                    InstanceId = sendEventAction.Instance.InstanceId,
                    Name = sendEventAction.EventName,
                    Input = sendEventAction.EventData,
                };

                runtimeState.AddEvent(sendEvent);

                newOrchestratorMessages ??= new List<TaskMessage>();
                newOrchestratorMessages.Add(new TaskMessage
                {
                    Event = sendEvent,
                    OrchestrationInstance = runtimeState.OrchestrationInstance,
                });
            }
            else if (action is OrchestrationCompleteOrchestratorAction completeAction)
            {
                if (completeAction.OrchestrationStatus == OrchestrationStatus.ContinuedAsNew)
                {
                    // Replace the existing runtime state with a complete new runtime state.
                    OrchestrationRuntimeState newRuntimeState = new();
                    newRuntimeState.AddEvent(new OrchestratorStartedEvent(-1));
                    newRuntimeState.AddEvent(new ExecutionStartedEvent(-1, completeAction.Result)
                    {
                        OrchestrationInstance = new OrchestrationInstance
                        {
                            InstanceId = runtimeState.OrchestrationInstance.InstanceId,
                            ExecutionId = Guid.NewGuid().ToString("N"),
                        },
                        Tags = runtimeState.Tags,
                        ParentInstance = runtimeState.ParentInstance,
                        Name = runtimeState.Name,
                        Version = completeAction.NewVersion ?? runtimeState.Version
                    });
                    newRuntimeState.Status = runtimeState.Status;

                    // The orchestration may have completed with some pending events that need to be carried
                    // over to the new generation, such as unprocessed external event messages.
                    if (completeAction.CarryoverEvents != null)
                    {
                        foreach (HistoryEvent carryoverEvent in completeAction.CarryoverEvents)
                        {
                            newRuntimeState.AddEvent(carryoverEvent);
                        }
                    }

                    runtimeState = newRuntimeState;
                    activityMessages = Array.Empty<TaskMessage>();
                    orchestratorMessages = Array.Empty<TaskMessage>();
                    timerMessages = Array.Empty<TaskMessage>();
                    continueAsNew = true;
                    updatedStatus = null;
                    return;
                }
                else
                {
                    this.log.OrchestratorCompleted(
                        runtimeState.OrchestrationInstance.InstanceId,
                        runtimeState.Name,
                        completeAction.OrchestrationStatus,
                        Encoding.UTF8.GetByteCount(completeAction.Result ?? string.Empty));
                }

                if (completeAction.OrchestrationStatus == OrchestrationStatus.Failed)
                {
                    failureDetails = completeAction.FailureDetails;
                }

                // NOTE: Failure details aren't being stored in the orchestration history, currently.
                runtimeState.AddEvent(new ExecutionCompletedEvent(
                    completeAction.Id,
                    completeAction.Result,
                    completeAction.OrchestrationStatus));

                // CONSIDER: Add support for fire-and-forget sub-orchestrations where
                //           we don't notify the parent that the orchestration completed.
                if (runtimeState.ParentInstance != null)
                {
                    HistoryEvent subOrchestratorCompletedEvent;
                    if (completeAction.OrchestrationStatus == OrchestrationStatus.Completed)
                    {
                        subOrchestratorCompletedEvent = new SubOrchestrationInstanceCompletedEvent(
                            eventId: -1,
                            runtimeState.ParentInstance.TaskScheduleId,
                            completeAction.Result);
                    }
                    else
                    {
                        subOrchestratorCompletedEvent = new SubOrchestrationInstanceFailedEvent(
                            eventId: -1,
                            runtimeState.ParentInstance.TaskScheduleId,
                            completeAction.Result,
                            completeAction.Details,
                            completeAction.FailureDetails);
                    }

                    newOrchestratorMessages ??= new List<TaskMessage>();
                    newOrchestratorMessages.Add(new TaskMessage
                    {
                        Event = subOrchestratorCompletedEvent,
                        OrchestrationInstance = runtimeState.ParentInstance.OrchestrationInstance,
                    });
                }
            }
            else
            {
                this.log.IgnoringUnknownOrchestratorAction(
                    runtimeState.OrchestrationInstance.InstanceId,
                    action.OrchestratorActionType);
            }
        }

        runtimeState.AddEvent(new OrchestratorCompletedEvent(-1));

        activityMessages = newActivityMessages ?? Array.Empty<TaskMessage>();
        timerMessages = newTimerMessages ?? Array.Empty<TaskMessage>();
        orchestratorMessages = newOrchestratorMessages ?? Array.Empty<TaskMessage>();

        updatedStatus = new OrchestrationState
        {
            OrchestrationInstance = runtimeState.OrchestrationInstance,
            ParentInstance = runtimeState.ParentInstance,
            Name = runtimeState.Name,
            Version = runtimeState.Version,
            Status = runtimeState.Status,
            Tags = runtimeState.Tags,
            OrchestrationStatus = runtimeState.OrchestrationStatus,
            CreatedTime = runtimeState.CreatedTime,
            CompletedTime = runtimeState.CompletedTime,
            LastUpdatedTime = DateTime.UtcNow,
            Size = runtimeState.Size,
            CompressedSize = runtimeState.CompressedSize,
            Input = runtimeState.Input,
            Output = runtimeState.Output,
            ScheduledStartTime = runtimeState.ExecutionStartedEvent?.ScheduledStartTime,
            FailureDetails = failureDetails,
        };
    }

    static string GetShortHistoryEventDescription(HistoryEvent e)
    {
        if (Utils.TryGetTaskScheduledId(e, out int taskScheduledId))
        {
            return $"{e.EventType}#{taskScheduledId}";
        }
        else
        {
            return e.EventType.ToString();
        }
    }

    public override int GetDelayInSecondsOnFetchException(Exception ex) =>
        this.service.GetDelayInSecondsAfterOnFetchException(ex);

    public override string GetWorkItemId(TaskOrchestrationWorkItem workItem) => workItem.InstanceId;

    public override Task ReleaseWorkItemAsync(TaskOrchestrationWorkItem workItem) =>
        this.service.ReleaseTaskOrchestrationWorkItemAsync(workItem);

    public override async Task<TaskOrchestrationWorkItem> RenewWorkItemAsync(TaskOrchestrationWorkItem workItem)
    {
        await this.service.RenewTaskOrchestrationWorkItemLockAsync(workItem);
        return workItem;
    }
}
