// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc;

/// <summary>
/// Builds the worker response for an orchestration rewind request.
/// </summary>
static class RewindOrchestrationHandler
{
    /// <summary>
    /// Creates an orchestrator response containing the replacement history for a rewind.
    /// </summary>
    /// <param name="request">The orchestrator request.</param>
    /// <param name="pastEvents">The complete committed orchestration history.</param>
    /// <param name="completionToken">The completion token for the work item.</param>
    /// <param name="orchestrationActivity">The current orchestration trace activity.</param>
    /// <returns>The response containing a single rewind action.</returns>
    internal static P.OrchestratorResponse CreateResponse(
        P.OrchestratorRequest request,
        IReadOnlyList<P.HistoryEvent> pastEvents,
        string completionToken,
        Activity? orchestrationActivity)
    {
        Check.NotNull(request);
        Check.NotNull(pastEvents);

        if (request.NewEvents.Count != 2
            || request.NewEvents[0].EventTypeCase != P.HistoryEvent.EventTypeOneofCase.OrchestratorStarted
            || request.NewEvents[1].EventTypeCase != P.HistoryEvent.EventTypeOneofCase.ExecutionRewound)
        {
            throw new InvalidOperationException(
                "When rewinding an orchestration, the new events list must contain exactly two events: " +
                "OrchestratorStarted and ExecutionRewound.");
        }

        P.ExecutionRewoundEvent rewindEvent = request.NewEvents[1].ExecutionRewound;
        List<P.HistoryEvent> allEvents = [.. pastEvents, .. request.NewEvents];
        P.HistoryEvent? executionStartedEvent = allEvents.FirstOrDefault(
            e => e.EventTypeCase == P.HistoryEvent.EventTypeOneofCase.ExecutionStarted);

        ActivityContext childTraceContextSource = orchestrationActivity?.Context ?? default;
        bool hasChildTraceContextSource = orchestrationActivity is not null;

        HashSet<int> failedTaskIds = [];
        foreach (P.HistoryEvent historyEvent in allEvents)
        {
            if (historyEvent.EventTypeCase == P.HistoryEvent.EventTypeOneofCase.TaskFailed)
            {
                failedTaskIds.Add(historyEvent.TaskFailed.TaskScheduledId);
            }
            else if (historyEvent.EventTypeCase == P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceFailed)
            {
                failedTaskIds.Add(historyEvent.SubOrchestrationInstanceFailed.TaskScheduledId);
            }
        }

        string newExecutionId = Guid.NewGuid().ToString("N");
        P.RewindOrchestrationAction rewindAction = new();

        // Retry timers are retained to match the existing rewind protocol. Rewinding failed activities
        // that were scheduled with retry policies is not currently supported.
        foreach (P.HistoryEvent historyEvent in allEvents)
        {
            // Do not add any failed tasks or the failed execution completed event to the new history
            if (historyEvent.EventTypeCase == P.HistoryEvent.EventTypeOneofCase.TaskFailed
                || historyEvent.EventTypeCase == P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceFailed
                || historyEvent.EventTypeCase == P.HistoryEvent.EventTypeOneofCase.ExecutionCompleted
                || (historyEvent.EventTypeCase == P.HistoryEvent.EventTypeOneofCase.TaskScheduled
                    && failedTaskIds.Contains(historyEvent.EventId)))
            {
                continue;
            }

            // Modify the execution started event to reflect the new execution ID and new parent execution ID (if applicable)
            if (historyEvent.EventTypeCase == P.HistoryEvent.EventTypeOneofCase.ExecutionStarted)
            {
                P.HistoryEvent eventCopy = historyEvent.Clone();
                eventCopy.ExecutionStarted.OrchestrationInstance.ExecutionId = newExecutionId;

                if (!string.IsNullOrEmpty(rewindEvent.ParentExecutionId)
                    && eventCopy.ExecutionStarted.ParentInstance?.OrchestrationInstance is { } parentInstance)
                {
                    parentInstance.ExecutionId = rewindEvent.ParentExecutionId;
                }

                if (rewindEvent.ParentTraceContext is not null)
                {
                    eventCopy.ExecutionStarted.ParentTraceContext = rewindEvent.ParentTraceContext.Clone();
                }

                if (!hasChildTraceContextSource)
                {
                    hasChildTraceContextSource = ActivityContext.TryParse(
                        eventCopy.ExecutionStarted.ParentTraceContext?.TraceParent,
                        eventCopy.ExecutionStarted.ParentTraceContext?.TraceState,
                        out childTraceContextSource);
                }

                rewindAction.NewHistory.Add(eventCopy);
                continue;
            }

            if (historyEvent.EventTypeCase == P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCreated
                && failedTaskIds.Contains(historyEvent.EventId)
                && hasChildTraceContextSource)
            {
                ActivityContext newParentTraceContext = new(
                    childTraceContextSource.TraceId,
                    ActivitySpanId.CreateRandom(),
                    childTraceContextSource.TraceFlags,
                    childTraceContextSource.TraceState);

                P.HistoryEvent eventCopy = historyEvent.Clone();
                eventCopy.SubOrchestrationInstanceCreated.ParentTraceContext =
                    ProtoUtils.CreateTraceContext(newParentTraceContext);
                rewindAction.NewHistory.Add(eventCopy);
                continue;
            }

            rewindAction.NewHistory.Add(historyEvent);
        }

        return new P.OrchestratorResponse
        {
            InstanceId = request.InstanceId,
            CompletionToken = completionToken,
            OrchestrationTraceContext = new()
            {
                SpanID = orchestrationActivity?.SpanId.ToString(),
                SpanStartTime = orchestrationActivity?.StartTimeUtc.ToTimestamp(),
            },
            Actions =
            {
                new P.OrchestratorAction
                {
                    Id = -1,
                    RewindOrchestration = rewindAction,
                },
            },
        };
    }
}
