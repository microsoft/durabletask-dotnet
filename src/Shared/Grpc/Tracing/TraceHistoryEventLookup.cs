// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Tracing;

/// <summary>
/// Provides indexed lookups of past history events by event ID, used to correlate new completion/failure
/// events (e.g. "TaskCompleted") back to the history event that scheduled them (e.g. "TaskScheduled").
/// </summary>
/// <remarks>
/// The indexes are built lazily, at most once per event type, and cached for the lifetime of the orchestrator
/// work item. This avoids re-scanning the full set of past events for every new event being processed in a work
/// item, which would otherwise be O(new events x past events) for work items with many new events.
/// </remarks>
sealed class TraceHistoryEventLookup
{
    readonly IEnumerable<P.HistoryEvent> pastEvents;

    Dictionary<int, P.HistoryEvent>? taskScheduledEventsByEventId;
    Dictionary<int, P.HistoryEvent>? subOrchestrationInstanceCreatedEventsByEventId;

    /// <summary>
    /// Initializes a new instance of the <see cref="TraceHistoryEventLookup"/> class.
    /// </summary>
    /// <param name="pastEvents">The past history events for the current orchestrator work item.</param>
    public TraceHistoryEventLookup(IEnumerable<P.HistoryEvent> pastEvents)
    {
        this.pastEvents = pastEvents;
    }

    /// <summary>
    /// Gets the "TaskScheduled" history event with the given event ID, if any.
    /// </summary>
    /// <param name="eventId">The event ID to look up.</param>
    /// <returns>The matching event, or <see langword="null"/> if none is found.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when more than one "TaskScheduled" event has the same event ID.
    /// </exception>
    public P.HistoryEvent? GetTaskScheduledEvent(int eventId)
    {
        this.taskScheduledEventsByEventId ??= BuildIndex(
            this.pastEvents, P.HistoryEvent.EventTypeOneofCase.TaskScheduled);
        return this.taskScheduledEventsByEventId.TryGetValue(eventId, out P.HistoryEvent? historyEvent)
            ? historyEvent
            : null;
    }

    /// <summary>
    /// Gets the "SubOrchestrationInstanceCreated" history event with the given event ID, if any.
    /// </summary>
    /// <param name="eventId">The event ID to look up.</param>
    /// <returns>The matching event, or <see langword="null"/> if none is found.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when more than one "SubOrchestrationInstanceCreated" event has the same event ID.
    /// </exception>
    public P.HistoryEvent? GetSubOrchestrationInstanceCreatedEvent(int eventId)
    {
        this.subOrchestrationInstanceCreatedEventsByEventId ??= BuildIndex(
            this.pastEvents, P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCreated);
        return this.subOrchestrationInstanceCreatedEventsByEventId.TryGetValue(eventId, out P.HistoryEvent? historyEvent)
            ? historyEvent
            : null;
    }

    static Dictionary<int, P.HistoryEvent> BuildIndex(
        IEnumerable<P.HistoryEvent> events, P.HistoryEvent.EventTypeOneofCase eventType)
    {
        Dictionary<int, P.HistoryEvent> index = new();
        foreach (P.HistoryEvent historyEvent in events)
        {
            if (historyEvent.EventTypeCase != eventType)
            {
                continue;
            }

            try
            {
                index.Add(historyEvent.EventId, historyEvent);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidOperationException(
                    $"Past orchestration history contains multiple '{eventType}' events with event ID "
                    + $"'{historyEvent.EventId}'.",
                    exception);
            }
        }

        return index;
    }
}
