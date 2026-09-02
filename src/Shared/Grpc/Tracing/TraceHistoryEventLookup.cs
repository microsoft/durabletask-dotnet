// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Tracing;

/// <summary>
/// Provides indexed lookups of past history events by event ID, used to correlate new completion/failure
/// events (e.g. "TaskCompleted") back to the history event that scheduled them (e.g. "TaskScheduled").
/// </summary>
/// <remarks>
/// The indexes contain only scheduling event IDs referenced by the current work item's new completion/failure
/// events. They are built together in one lazy scan and cached for the lifetime of the orchestrator work item.
/// This avoids both re-scanning the full set of past events for every new event and retaining unrelated history
/// events, bounding lookup storage by the number of correlation IDs in the current work item.
/// </remarks>
sealed class TraceHistoryEventLookup
{
    readonly IEnumerable<P.HistoryEvent> pastEvents;

    readonly Dictionary<int, P.HistoryEvent?>? taskScheduledEventsByEventId;
    readonly Dictionary<int, P.HistoryEvent?>? subOrchestrationInstanceCreatedEventsByEventId;

    HashSet<int>? duplicateTaskScheduledEventIds;
    HashSet<int>? duplicateSubOrchestrationInstanceCreatedEventIds;
    bool indexesBuilt;

    /// <summary>
    /// Initializes a new instance of the <see cref="TraceHistoryEventLookup"/> class.
    /// </summary>
    /// <param name="pastEvents">The past history events for the current orchestrator work item.</param>
    /// <param name="newEvents">The new history events whose correlation IDs may be looked up.</param>
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
                    this.taskScheduledEventsByEventId ??= new();
                    this.taskScheduledEventsByEventId[newEvent.TaskCompleted.TaskScheduledId] = null;
                    break;

                case P.HistoryEvent.EventTypeOneofCase.TaskFailed:
                    this.taskScheduledEventsByEventId ??= new();
                    this.taskScheduledEventsByEventId[newEvent.TaskFailed.TaskScheduledId] = null;
                    break;

                case P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCompleted:
                    this.subOrchestrationInstanceCreatedEventsByEventId ??= new();
                    this.subOrchestrationInstanceCreatedEventsByEventId[
                        newEvent.SubOrchestrationInstanceCompleted.TaskScheduledId] = null;
                    break;

                case P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceFailed:
                    this.subOrchestrationInstanceCreatedEventsByEventId ??= new();
                    this.subOrchestrationInstanceCreatedEventsByEventId[
                        newEvent.SubOrchestrationInstanceFailed.TaskScheduledId] = null;
                    break;
            }
        }
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
        this.BuildIndexes();
        ThrowIfDuplicate(
            this.duplicateTaskScheduledEventIds,
            eventId,
            P.HistoryEvent.EventTypeOneofCase.TaskScheduled);

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
    /// <exception cref="InvalidOperationException">
    /// Thrown when more than one "SubOrchestrationInstanceCreated" event has the same event ID.
    /// </exception>
    public P.HistoryEvent? GetSubOrchestrationInstanceCreatedEvent(int eventId)
    {
        this.BuildIndexes();
        ThrowIfDuplicate(
            this.duplicateSubOrchestrationInstanceCreatedEventIds,
            eventId,
            P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCreated);

        return this.subOrchestrationInstanceCreatedEventsByEventId is not null
            && this.subOrchestrationInstanceCreatedEventsByEventId.TryGetValue(
                eventId, out P.HistoryEvent? historyEvent)
            ? historyEvent
            : null;
    }

    static void IndexRequestedEvent(
        Dictionary<int, P.HistoryEvent?>? index,
        ref HashSet<int>? duplicateEventIds,
        P.HistoryEvent historyEvent)
    {
        if (index is null
            || !index.TryGetValue(historyEvent.EventId, out P.HistoryEvent? existingEvent))
        {
            return;
        }

        if (existingEvent is null)
        {
            index[historyEvent.EventId] = historyEvent;
        }
        else
        {
            duplicateEventIds ??= new();
            duplicateEventIds.Add(historyEvent.EventId);
        }
    }

    static void ThrowIfDuplicate(
        HashSet<int>? duplicateEventIds,
        int eventId,
        P.HistoryEvent.EventTypeOneofCase eventType)
    {
        if (duplicateEventIds?.Contains(eventId) == true)
        {
            throw new InvalidOperationException(
                $"Past orchestration history contains multiple '{eventType}' events with event ID '{eventId}'.");
        }
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
                    IndexRequestedEvent(
                        this.taskScheduledEventsByEventId,
                        ref this.duplicateTaskScheduledEventIds,
                        historyEvent);
                    break;

                case P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCreated:
                    IndexRequestedEvent(
                        this.subOrchestrationInstanceCreatedEventsByEventId,
                        ref this.duplicateSubOrchestrationInstanceCreatedEventIds,
                        historyEvent);
                    break;
            }
        }

        this.indexesBuilt = true;
    }
}
