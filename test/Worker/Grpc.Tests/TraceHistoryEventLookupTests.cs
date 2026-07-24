// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Tracing;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

public class TraceHistoryEventLookupTests
{
    [Fact]
    public void GetTaskScheduledEvent_DuplicateEventIds_ReturnsLastMatch()
    {
        // Arrange
        List<P.HistoryEvent> pastEvents =
        [
            CreateTaskScheduled(eventId: 1, name: "FirstScheduled"),
            CreateTaskScheduled(eventId: 1, name: "SecondScheduled"),
        ];
        TraceHistoryEventLookup lookup = new(pastEvents);

        // Act
        P.HistoryEvent? result = lookup.GetTaskScheduledEvent(1);

        // Assert
        result.Should().NotBeNull();
        result!.TaskScheduled.Name.Should().Be("SecondScheduled");
    }

    [Fact]
    public void GetSubOrchestrationInstanceCreatedEvent_DuplicateEventIds_ReturnsFirstMatch()
    {
        // Arrange
        List<P.HistoryEvent> pastEvents =
        [
            CreateSubOrchestrationInstanceCreated(eventId: 2, name: "FirstSub"),
            CreateSubOrchestrationInstanceCreated(eventId: 2, name: "SecondSub"),
        ];
        TraceHistoryEventLookup lookup = new(pastEvents);

        // Act
        P.HistoryEvent? result = lookup.GetSubOrchestrationInstanceCreatedEvent(2);

        // Assert
        result.Should().NotBeNull();
        result!.SubOrchestrationInstanceCreated.Name.Should().Be("FirstSub");
    }

    [Fact]
    public void GetTaskScheduledEvent_NoMatch_ReturnsNull()
    {
        // Arrange
        List<P.HistoryEvent> pastEvents = [CreateTaskScheduled(eventId: 1, name: "Scheduled")];
        TraceHistoryEventLookup lookup = new(pastEvents);

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
        TraceHistoryEventLookup lookup = new(pastEvents);

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
        TraceHistoryEventLookup lookup = new(pastEvents);

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
        TraceHistoryEventLookup lookup = new([]);

        // Act
        P.HistoryEvent? result = lookup.GetTaskScheduledEvent(0);

        // Assert
        result.Should().BeNull();
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
