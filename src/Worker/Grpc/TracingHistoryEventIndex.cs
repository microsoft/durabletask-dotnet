// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc;

/// <summary>
/// Indexes the orchestration history events used to reconstruct tracing spans.
/// </summary>
sealed class TracingHistoryEventIndex
{
    readonly Dictionary<int, P.HistoryEvent> subOrchestrationCreatedEvents = new();
    readonly Dictionary<int, P.HistoryEvent> taskScheduledEvents = new();

    public TracingHistoryEventIndex(IEnumerable<P.HistoryEvent> pastEvents)
    {
        foreach (P.HistoryEvent historyEvent in pastEvents)
        {
            switch (historyEvent.EventTypeCase)
            {
                case P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCreated:
                    // Preserve the previous FirstOrDefault semantics for duplicate IDs.
                    if (!this.subOrchestrationCreatedEvents.ContainsKey(historyEvent.EventId))
                    {
                        this.subOrchestrationCreatedEvents.Add(historyEvent.EventId, historyEvent);
                    }

                    break;

                case P.HistoryEvent.EventTypeOneofCase.TaskScheduled:
                    // Preserve the previous LastOrDefault semantics for duplicate IDs.
                    this.taskScheduledEvents[historyEvent.EventId] = historyEvent;
                    break;
            }
        }
    }

    public P.HistoryEvent? GetSubOrchestrationInstanceCreatedEvent(int eventId)
        => this.subOrchestrationCreatedEvents.TryGetValue(eventId, out P.HistoryEvent? historyEvent)
            ? historyEvent
            : null;

    public P.HistoryEvent? GetTaskScheduledEvent(int eventId)
        => this.taskScheduledEvents.TryGetValue(eventId, out P.HistoryEvent? historyEvent)
            ? historyEvent
            : null;
}
