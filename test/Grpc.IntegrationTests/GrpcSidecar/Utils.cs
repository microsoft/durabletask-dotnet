// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core.History;

namespace Dapr.DurableTask.Sidecar;

internal static class Utils
{
    public static bool TryGetTaskScheduledId(HistoryEvent historyEvent, out int taskScheduledId)
    {
        switch (historyEvent.EventType)
        {
            case EventType.TaskCompleted:
                taskScheduledId = ((TaskCompletedEvent)historyEvent).TaskScheduledId;
                return true;
            case EventType.TaskFailed:
                taskScheduledId = ((TaskFailedEvent)historyEvent).TaskScheduledId;
                return true;
            case EventType.SubOrchestrationInstanceCompleted:
                taskScheduledId = ((SubOrchestrationInstanceCompletedEvent)historyEvent).TaskScheduledId;
                return true;
            case EventType.SubOrchestrationInstanceFailed:
                taskScheduledId = ((SubOrchestrationInstanceFailedEvent)historyEvent).TaskScheduledId;
                return true;
            case EventType.TimerFired:
                taskScheduledId = ((TimerFiredEvent)historyEvent).TimerId;
                return true;
            case EventType.ExecutionStarted:
                var parentInstance = ((ExecutionStartedEvent)historyEvent).ParentInstance;
                if (parentInstance != null)
                {
                    // taskId that scheduled a sub-orchestration
                    taskScheduledId = parentInstance.TaskScheduleId;
                    return true;
                }
                else
                {
                    taskScheduledId = -1;
                    return false;
                }
            default:
                taskScheduledId = -1;
                return false;
        }
    }
}
