// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Tracing;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

public class TraceHistoryEventLookupTests
{
    [Fact]
    public void GetTaskScheduledEvent_DuplicateEventIds_Throws()
    {
        // Arrange
        List<P.HistoryEvent> pastEvents =
        [
            CreateTaskScheduled(eventId: 1, name: "FirstScheduled"),
            CreateTaskScheduled(eventId: 1, name: "SecondScheduled"),
        ];
        TraceHistoryEventLookup lookup = CreateLookup(pastEvents, taskScheduledEventIds: [1]);

        // Act
        Action act = () => lookup.GetTaskScheduledEvent(1);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'TaskScheduled'*event ID '1'*");
    }

    [Fact]
    public void GetSubOrchestrationInstanceCreatedEvent_DuplicateEventIds_Throws()
    {
        // Arrange
        List<P.HistoryEvent> pastEvents =
        [
            CreateSubOrchestrationInstanceCreated(eventId: 2, name: "FirstSub"),
            CreateSubOrchestrationInstanceCreated(eventId: 2, name: "SecondSub"),
        ];
        TraceHistoryEventLookup lookup = CreateLookup(
            pastEvents, subOrchestrationInstanceCreatedEventIds: [2]);

        // Act
        Action act = () => lookup.GetSubOrchestrationInstanceCreatedEvent(2);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'SubOrchestrationInstanceCreated'*event ID '2'*");
    }

    [Fact]
    public void GetTaskScheduledEvent_NoMatch_ReturnsNull()
    {
        // Arrange
        List<P.HistoryEvent> pastEvents = [CreateTaskScheduled(eventId: 1, name: "Scheduled")];
        TraceHistoryEventLookup lookup = CreateLookup(pastEvents, taskScheduledEventIds: [99]);

        // Act
        P.HistoryEvent? result = lookup.GetTaskScheduledEvent(99);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetSubOrchestrationInstanceCreatedEvent_NoMatch_ReturnsNull()
    {
        // Arrange
        List<P.HistoryEvent> pastEvents = [CreateSubOrchestrationInstanceCreated(eventId: 2, name: "Sub")];
        TraceHistoryEventLookup lookup = CreateLookup(
            pastEvents, subOrchestrationInstanceCreatedEventIds: [99]);

        // Act
        P.HistoryEvent? result = lookup.GetSubOrchestrationInstanceCreatedEvent(99);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetTaskScheduledEvent_IgnoresOtherEventTypesWithSameEventId()
    {
        // Arrange: a SubOrchestrationInstanceCreated event shares the event ID of the TaskScheduled event we
        // look up, but must not be returned since it is a different history event type.
        List<P.HistoryEvent> pastEvents =
        [
            CreateSubOrchestrationInstanceCreated(eventId: 5, name: "Sub"),
            CreateTaskScheduled(eventId: 5, name: "Scheduled"),
        ];
        TraceHistoryEventLookup lookup = CreateLookup(
            pastEvents,
            taskScheduledEventIds: [5],
            subOrchestrationInstanceCreatedEventIds: [5]);

        // Act
        P.HistoryEvent? taskScheduled = lookup.GetTaskScheduledEvent(5);
        P.HistoryEvent? subOrchestrationCreated = lookup.GetSubOrchestrationInstanceCreatedEvent(5);

        // Assert
        taskScheduled.Should().NotBeNull();
        taskScheduled!.TaskScheduled.Name.Should().Be("Scheduled");
        subOrchestrationCreated.Should().NotBeNull();
        subOrchestrationCreated!.SubOrchestrationInstanceCreated.Name.Should().Be("Sub");
    }

    [Fact]
    public void GetTaskScheduledEvent_EmptyPastEvents_ReturnsNull()
    {
        // Arrange
        TraceHistoryEventLookup lookup = CreateLookup([], taskScheduledEventIds: [0]);

        // Act
        P.HistoryEvent? result = lookup.GetTaskScheduledEvent(0);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetEvents_IndexesOnlyIdsReferencedByNewEvents()
    {
        // Arrange
        List<P.HistoryEvent> pastEvents =
        [
            CreateTaskScheduled(eventId: 1, name: "UnreferencedTask1"),
            CreateTaskScheduled(eventId: 1, name: "UnreferencedTask2"),
            CreateSubOrchestrationInstanceCreated(eventId: 2, name: "UnreferencedSub1"),
            CreateSubOrchestrationInstanceCreated(eventId: 2, name: "UnreferencedSub2"),
            CreateTaskScheduled(eventId: 3, name: "ReferencedTask"),
            CreateSubOrchestrationInstanceCreated(eventId: 4, name: "ReferencedSub"),
        ];
        TraceHistoryEventLookup lookup = CreateLookup(
            pastEvents,
            taskScheduledEventIds: [3],
            subOrchestrationInstanceCreatedEventIds: [4]);

        // Act
        P.HistoryEvent? taskScheduled = lookup.GetTaskScheduledEvent(3);
        P.HistoryEvent? subOrchestrationCreated = lookup.GetSubOrchestrationInstanceCreatedEvent(4);

        // Assert
        taskScheduled!.TaskScheduled.Name.Should().Be("ReferencedTask");
        subOrchestrationCreated!.SubOrchestrationInstanceCreated.Name.Should().Be("ReferencedSub");
    }

    [Fact]
    public void GetEventsOfBothTypes_EnumeratesPastEventsOnce()
    {
        // Arrange
        int enumerationCount = 0;
        TraceHistoryEventLookup lookup = CreateLookup(
            EnumeratePastEvents(),
            taskScheduledEventIds: [1],
            subOrchestrationInstanceCreatedEventIds: [2]);

        // Act
        P.HistoryEvent? taskScheduled = lookup.GetTaskScheduledEvent(1);
        P.HistoryEvent? subOrchestrationCreated = lookup.GetSubOrchestrationInstanceCreatedEvent(2);

        // Assert
        taskScheduled.Should().NotBeNull();
        subOrchestrationCreated.Should().NotBeNull();
        enumerationCount.Should().Be(1);

        IEnumerable<P.HistoryEvent> EnumeratePastEvents()
        {
            enumerationCount++;
            yield return CreateTaskScheduled(eventId: 1, name: "Scheduled");
            yield return CreateSubOrchestrationInstanceCreated(eventId: 2, name: "Sub");
        }
    }

    [Fact]
    public void Constructor_DoesNotEnumeratePastEvents()
    {
        // Arrange
        int enumerationCount = 0;

        // Act
        TraceHistoryEventLookup lookup = CreateLookup(
            EnumeratePastEvents(), taskScheduledEventIds: [1]);

        // Assert
        lookup.Should().NotBeNull();
        enumerationCount.Should().Be(0);

        IEnumerable<P.HistoryEvent> EnumeratePastEvents()
        {
            enumerationCount++;
            yield return CreateTaskScheduled(eventId: 1, name: "Scheduled");
        }
    }

    [Fact]
    public void GetEvents_RegistersFailureCorrelationIds()
    {
        // Arrange
        List<P.HistoryEvent> pastEvents =
        [
            CreateTaskScheduled(eventId: 1, name: "Scheduled"),
            CreateSubOrchestrationInstanceCreated(eventId: 2, name: "Sub"),
        ];
        TraceHistoryEventLookup lookup = CreateLookup(
            pastEvents,
            taskScheduledEventIds: [1],
            subOrchestrationInstanceCreatedEventIds: [2],
            useFailureEvents: true);

        // Act
        P.HistoryEvent? taskScheduled = lookup.GetTaskScheduledEvent(1);
        P.HistoryEvent? subOrchestrationCreated = lookup.GetSubOrchestrationInstanceCreatedEvent(2);

        // Assert
        taskScheduled!.TaskScheduled.Name.Should().Be("Scheduled");
        subOrchestrationCreated!.SubOrchestrationInstanceCreated.Name.Should().Be("Sub");
    }

    [Fact]
    public void GetEvents_DuplicateRequestedId_ThrowsOnlyForThatIdAndType()
    {
        // Arrange
        List<P.HistoryEvent> pastEvents =
        [
            CreateTaskScheduled(eventId: 1, name: "DuplicateTask1"),
            CreateTaskScheduled(eventId: 1, name: "DuplicateTask2"),
            CreateTaskScheduled(eventId: 3, name: "ValidTask"),
            CreateSubOrchestrationInstanceCreated(eventId: 2, name: "DuplicateSub1"),
            CreateSubOrchestrationInstanceCreated(eventId: 2, name: "DuplicateSub2"),
            CreateSubOrchestrationInstanceCreated(eventId: 4, name: "ValidSub"),
        ];
        TraceHistoryEventLookup lookup = CreateLookup(
            pastEvents,
            taskScheduledEventIds: [1, 3],
            subOrchestrationInstanceCreatedEventIds: [2, 4]);

        // Act
        P.HistoryEvent? validTask = lookup.GetTaskScheduledEvent(3);
        P.HistoryEvent? validSub = lookup.GetSubOrchestrationInstanceCreatedEvent(4);
        Action getDuplicateTask = () => lookup.GetTaskScheduledEvent(1);
        Action getDuplicateSub = () => lookup.GetSubOrchestrationInstanceCreatedEvent(2);

        // Assert
        validTask!.TaskScheduled.Name.Should().Be("ValidTask");
        validSub!.SubOrchestrationInstanceCreated.Name.Should().Be("ValidSub");
        getDuplicateTask.Should().Throw<InvalidOperationException>()
            .WithMessage("*'TaskScheduled'*event ID '1'*");
        getDuplicateSub.Should().Throw<InvalidOperationException>()
            .WithMessage("*'SubOrchestrationInstanceCreated'*event ID '2'*");
    }

    static TraceHistoryEventLookup CreateLookup(
        IEnumerable<P.HistoryEvent> pastEvents,
        IEnumerable<int>? taskScheduledEventIds = null,
        IEnumerable<int>? subOrchestrationInstanceCreatedEventIds = null,
        bool useFailureEvents = false)
    {
        List<P.HistoryEvent> newEvents = [];
        if (taskScheduledEventIds is not null)
        {
            foreach (int eventId in taskScheduledEventIds)
            {
                P.HistoryEvent newEvent = useFailureEvents
                    ? new P.HistoryEvent
                    {
                        TaskFailed = new P.TaskFailedEvent { TaskScheduledId = eventId },
                    }
                    : new P.HistoryEvent
                    {
                        TaskCompleted = new P.TaskCompletedEvent { TaskScheduledId = eventId },
                    };
                newEvents.Add(newEvent);
            }
        }

        if (subOrchestrationInstanceCreatedEventIds is not null)
        {
            foreach (int eventId in subOrchestrationInstanceCreatedEventIds)
            {
                P.HistoryEvent newEvent = useFailureEvents
                    ? new P.HistoryEvent
                    {
                        SubOrchestrationInstanceFailed =
                            new P.SubOrchestrationInstanceFailedEvent { TaskScheduledId = eventId },
                    }
                    : new P.HistoryEvent
                    {
                        SubOrchestrationInstanceCompleted =
                            new P.SubOrchestrationInstanceCompletedEvent { TaskScheduledId = eventId },
                    };
                newEvents.Add(newEvent);
            }
        }

        return new TraceHistoryEventLookup(pastEvents, newEvents);
    }

    static P.HistoryEvent CreateTaskScheduled(int eventId, string name)
    {
        return new P.HistoryEvent
        {
            EventId = eventId,
            TaskScheduled = new P.TaskScheduledEvent { Name = name },
        };
    }

    static P.HistoryEvent CreateSubOrchestrationInstanceCreated(int eventId, string name)
    {
        return new P.HistoryEvent
        {
            EventId = eventId,
            SubOrchestrationInstanceCreated = new P.SubOrchestrationInstanceCreatedEvent
            {
                InstanceId = $"sub-{eventId}",
                Name = name,
            },
        };
    }
}
