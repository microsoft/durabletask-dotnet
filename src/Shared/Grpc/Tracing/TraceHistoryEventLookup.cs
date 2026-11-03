// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Tracing;

/// <summary>
/// Provides indexed lookups of past history events by event ID, used to correlate new completion/failure
/// events (e.g. "TaskCompleted") back to the history event that scheduled them (e.g. "TaskScheduled").
/// </summary>
/// <remarks>
/// Both indexes contain all past scheduling events of their respective types. They are populated together
/// on the first lookup in one lazy scan and cached for the lifetime of the orchestrator work item.
/// </remarks>
sealed class TraceHistoryEventLookup
{
    readonly IEnumerable<P.HistoryEvent> pastEvents;

    readonly Dictionary<int, P.HistoryEvent> taskScheduledEventsByEventId = new();
    readonly Dictionary<int, P.HistoryEvent> subOrchestrationInstanceCreatedEventsByEventId = new();

    bool indexesBuilt;

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
    public P.HistoryEvent? GetTaskScheduledEvent(int eventId)
    {
        this.BuildIndexes();
        return this.taskScheduledEventsByEventId.TryGetValue(eventId, out P.HistoryEvent? historyEvent)
            ? historyEvent
            : null;
    }

    /// <summary>
    /// Gets the "SubOrchestrationInstanceCreated" history event with the given event ID, if any.
    /// </summary>
    /// <param name="eventId">The event ID to look up.</param>
    /// <returns>The matching event, or <see langword="null"/> if none is found.</returns>
    public P.HistoryEvent? GetSubOrchestrationInstanceCreatedEvent(int eventId)
    {
        this.BuildIndexes();
        return this.subOrchestrationInstanceCreatedEventsByEventId.TryGetValue(
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
                    this.taskScheduledEventsByEventId.Add(historyEvent.EventId, historyEvent);
                    break;

                case P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCreated:
                    this.subOrchestrationInstanceCreatedEventsByEventId.Add(historyEvent.EventId, historyEvent);
                    break;
            }
        }

        this.indexesBuilt = true;
    }
}
