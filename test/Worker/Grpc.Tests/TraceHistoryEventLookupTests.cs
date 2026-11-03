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
        act.Should().Throw<ArgumentException>().WithMessage("*1*");
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
        act.Should().Throw<ArgumentException>().WithMessage("*2*");
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
    public void GetEvents_IndexesAllSchedulingEventsOfNeededTypes()
    {
        // Arrange
        List<P.HistoryEvent> pastEvents =
        [
            CreateTaskScheduled(eventId: 1, name: "UnreferencedTask"),
            CreateSubOrchestrationInstanceCreated(eventId: 2, name: "UnreferencedSub"),
            CreateTaskScheduled(eventId: 3, name: "ReferencedTask"),
            CreateSubOrchestrationInstanceCreated(eventId: 4, name: "ReferencedSub"),
        ];
        TraceHistoryEventLookup lookup = CreateLookup(
            pastEvents,
            taskScheduledEventIds: [3],
            subOrchestrationInstanceCreatedEventIds: [4]);

        // Act
        P.HistoryEvent? unreferencedTask = lookup.GetTaskScheduledEvent(1);
        P.HistoryEvent? unreferencedSub = lookup.GetSubOrchestrationInstanceCreatedEvent(2);
        P.HistoryEvent? referencedTask = lookup.GetTaskScheduledEvent(3);
        P.HistoryEvent? referencedSub = lookup.GetSubOrchestrationInstanceCreatedEvent(4);

        // Assert
        unreferencedTask.Should().BeSameAs(pastEvents[0]);
        unreferencedSub.Should().BeSameAs(pastEvents[1]);
        referencedTask.Should().BeSameAs(pastEvents[2]);
        referencedSub.Should().BeSameAs(pastEvents[3]);
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
    public void GetEvents_FailureCompletions_EnableBothIndexes()
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetEvents_DuplicateUnrequestedIdInNeededIndex_Throws(bool indexTaskEvents)
    {
        // Arrange
        List<P.HistoryEvent> pastEvents =
        [
            indexTaskEvents
                ? CreateTaskScheduled(eventId: 1, name: "First")
                : CreateSubOrchestrationInstanceCreated(eventId: 1, name: "First"),
            indexTaskEvents
                ? CreateTaskScheduled(eventId: 1, name: "Duplicate")
                : CreateSubOrchestrationInstanceCreated(eventId: 1, name: "Duplicate"),
            indexTaskEvents
                ? CreateTaskScheduled(eventId: 2, name: "Requested")
                : CreateSubOrchestrationInstanceCreated(eventId: 2, name: "Requested"),
        ];
        TraceHistoryEventLookup lookup = CreateLookup(
            pastEvents,
            taskScheduledEventIds: indexTaskEvents ? [2] : null,
            subOrchestrationInstanceCreatedEventIds: indexTaskEvents ? null : [2]);

        // Act
        Action act = () =>
        {
            _ = indexTaskEvents
                ? lookup.GetTaskScheduledEvent(2)
                : lookup.GetSubOrchestrationInstanceCreatedEvent(2);
        };

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*1*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetEvents_IgnoresUnneededSchedulingEventTypes(bool indexTaskEvents)
    {
        // Arrange
        List<P.HistoryEvent> pastEvents =
        [
            indexTaskEvents
                ? CreateTaskScheduled(eventId: 1, name: "Requested")
                : CreateSubOrchestrationInstanceCreated(eventId: 1, name: "Requested"),
            indexTaskEvents
                ? CreateSubOrchestrationInstanceCreated(eventId: 2, name: "First")
                : CreateTaskScheduled(eventId: 2, name: "First"),
            indexTaskEvents
                ? CreateSubOrchestrationInstanceCreated(eventId: 2, name: "Duplicate")
                : CreateTaskScheduled(eventId: 2, name: "Duplicate"),
        ];
        TraceHistoryEventLookup lookup = CreateLookup(
            pastEvents,
            taskScheduledEventIds: indexTaskEvents ? [1] : null,
            subOrchestrationInstanceCreatedEventIds: indexTaskEvents ? null : [1]);

        // Act
        P.HistoryEvent? result = indexTaskEvents
            ? lookup.GetTaskScheduledEvent(1)
            : lookup.GetSubOrchestrationInstanceCreatedEvent(1);

        // Assert
        result.Should().BeSameAs(pastEvents[0]);
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
            newEvents.AddRange(
                taskScheduledEventIds.Select(eventId => useFailureEvents
                    ? new P.HistoryEvent
                    {
                        TaskFailed = new P.TaskFailedEvent { TaskScheduledId = eventId },
                    }
                    : new P.HistoryEvent
                    {
                        TaskCompleted = new P.TaskCompletedEvent { TaskScheduledId = eventId },
                    }));
        }

        if (subOrchestrationInstanceCreatedEventIds is not null)
        {
            newEvents.AddRange(
                subOrchestrationInstanceCreatedEventIds.Select(eventId => useFailureEvents
                    ? new P.HistoryEvent
                    {
                        SubOrchestrationInstanceFailed =
                            new P.SubOrchestrationInstanceFailedEvent { TaskScheduledId = eventId },
                    }
                    : new P.HistoryEvent
                    {
                        SubOrchestrationInstanceCompleted =
                            new P.SubOrchestrationInstanceCompletedEvent { TaskScheduledId = eventId },
                    }));
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
