// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Tracing;

/// <summary>
/// Provides indexed lookups of past history events by event ID, used to correlate new completion/failure
/// events (e.g. "TaskCompleted") back to the history event that scheduled them (e.g. "TaskScheduled").
/// </summary>
/// <remarks>
/// Indexes are created only for scheduling event types needed by the current work item's new completion/failure
/// events. Each needed index contains all past scheduling events of that type. The indexes are populated together
/// in one lazy scan and cached for the lifetime of the orchestrator work item.
/// </remarks>
sealed class TraceHistoryEventLookup
{
    readonly IEnumerable<P.HistoryEvent> pastEvents;

    readonly Dictionary<int, P.HistoryEvent>? taskScheduledEventsByEventId;
    readonly Dictionary<int, P.HistoryEvent>? subOrchestrationInstanceCreatedEventsByEventId;

    bool indexesBuilt;

    /// <summary>
    /// Initializes a new instance of the <see cref="TraceHistoryEventLookup"/> class.
    /// </summary>
    /// <param name="pastEvents">The past history events for the current orchestrator work item.</param>
    /// <param name="newEvents">The new history events that determine which scheduling event types need indexes.</param>
    public TraceHistoryEventLookup(
        IEnumerable<P.HistoryEvent> pastEvents,
        IEnumerable<P.HistoryEvent> newEvents)
    {
        this.pastEvents = pastEvents;

        foreach (P.HistoryEvent newEvent in newEvents)
        {
            switch (newEvent.EventTypeCase)
            {
                case P.HistoryEvent.EventTypeOneofCase.TaskCompleted:
                case P.HistoryEvent.EventTypeOneofCase.TaskFailed:
                    this.taskScheduledEventsByEventId ??= new();
                    break;

                case P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCompleted:
                case P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceFailed:
                    this.subOrchestrationInstanceCreatedEventsByEventId ??= new();
                    break;
            }
        }
    }

    /// <summary>
    /// Gets the "TaskScheduled" history event with the given event ID, if any.
    /// </summary>
    /// <param name="eventId">The event ID to look up.</param>
    /// <returns>The matching event, or <see langword="null"/> if none is found.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when indexed scheduling events of the same type have duplicate event IDs.
    /// </exception>
    public P.HistoryEvent? GetTaskScheduledEvent(int eventId)
    {
        this.BuildIndexes();
        return this.taskScheduledEventsByEventId is not null
            && this.taskScheduledEventsByEventId.TryGetValue(eventId, out P.HistoryEvent? historyEvent)
            ? historyEvent
            : null;
    }

    /// <summary>
    /// Gets the "SubOrchestrationInstanceCreated" history event with the given event ID, if any.
    /// </summary>
    /// <param name="eventId">The event ID to look up.</param>
    /// <returns>The matching event, or <see langword="null"/> if none is found.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when indexed scheduling events of the same type have duplicate event IDs.
    /// </exception>
    public P.HistoryEvent? GetSubOrchestrationInstanceCreatedEvent(int eventId)
    {
        this.BuildIndexes();
        return this.subOrchestrationInstanceCreatedEventsByEventId is not null
            && this.subOrchestrationInstanceCreatedEventsByEventId.TryGetValue(
                eventId, out P.HistoryEvent? historyEvent)
            ? historyEvent
            : null;
    }

    void BuildIndexes()
    {
        if (this.indexesBuilt)
        {
            return;
        }

        foreach (P.HistoryEvent historyEvent in this.pastEvents)
        {
            switch (historyEvent.EventTypeCase)
            {
                case P.HistoryEvent.EventTypeOneofCase.TaskScheduled:
                    this.taskScheduledEventsByEventId?.Add(historyEvent.EventId, historyEvent);
                    break;

                case P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCreated:
                    this.subOrchestrationInstanceCreatedEventsByEventId?.Add(historyEvent.EventId, historyEvent);
                    break;
            }
        }

        this.indexesBuilt = true;
    }
}
