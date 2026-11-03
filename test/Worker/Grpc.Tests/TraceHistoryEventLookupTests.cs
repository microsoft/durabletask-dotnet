// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Tracing;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

public class TraceHistoryEventLookupTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetEvent_DuplicateSchedulingEventIds_Throws(bool taskEvents)
    {
        // Arrange
        TraceHistoryEventLookup lookup = new(
        [
            CreateSchedulingEvent(1, taskEvents),
            CreateSchedulingEvent(1, taskEvents),
        ]);

        // Act
        Action act = () => GetEvent(lookup, 1, taskEvents);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*1*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetEvent_NoMatch_ReturnsNull(bool taskEvents)
    {
        // Arrange
        TraceHistoryEventLookup lookup = new([CreateSchedulingEvent(1, taskEvents)]);

        // Act
        P.HistoryEvent? result = GetEvent(lookup, 99, taskEvents);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetEvent_EmptyHistory_ReturnsNull(bool taskEvents)
    {
        // Arrange
        TraceHistoryEventLookup lookup = new([]);

        // Act
        P.HistoryEvent? result = GetEvent(lookup, 0, taskEvents);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetEvents_IndexesBothSchedulingTypesWithTheSameEventId()
    {
        // Arrange
        P.HistoryEvent task = CreateSchedulingEvent(5, taskEvent: true);
        P.HistoryEvent sub = CreateSchedulingEvent(5, taskEvent: false);
        TraceHistoryEventLookup lookup = new(
        [
            sub,
            new P.HistoryEvent { EventId = 5, TaskCompleted = new P.TaskCompletedEvent() },
            new P.HistoryEvent { EventId = 5, TimerCreated = new P.TimerCreatedEvent() },
            task,
        ]);

        // Act
        P.HistoryEvent? taskScheduled = lookup.GetTaskScheduledEvent(5);
        P.HistoryEvent? subCreated = lookup.GetSubOrchestrationInstanceCreatedEvent(5);

        // Assert
        taskScheduled.Should().BeSameAs(task);
        subCreated.Should().BeSameAs(sub);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetEvents_EitherLookupBuildsBothIndexesInOnePass(bool taskFirst)
    {
        // Arrange
        P.HistoryEvent task = CreateSchedulingEvent(1, taskEvent: true);
        P.HistoryEvent sub = CreateSchedulingEvent(2, taskEvent: false);
        int enumerationCount = 0;
        TraceHistoryEventLookup lookup = new(EnumeratePastEvents());

        // Act
        P.HistoryEvent? first = GetEvent(lookup, taskFirst ? 1 : 2, taskFirst);
        P.HistoryEvent? second = GetEvent(lookup, taskFirst ? 2 : 1, !taskFirst);
        P.HistoryEvent? repeated = GetEvent(lookup, taskFirst ? 1 : 2, taskFirst);
        P.HistoryEvent? missing = GetEvent(lookup, 99, taskFirst);

        // Assert
        first.Should().BeSameAs(taskFirst ? task : sub);
        second.Should().BeSameAs(taskFirst ? sub : task);
        repeated.Should().BeSameAs(first);
        missing.Should().BeNull();
        enumerationCount.Should().Be(1);

        IEnumerable<P.HistoryEvent> EnumeratePastEvents()
        {
            enumerationCount++;
            yield return task;
            yield return sub;
        }
    }

    [Fact]
    public void Constructor_DoesNotEnumeratePastEvents()
    {
        // Arrange
        Mock<IEnumerable<P.HistoryEvent>> pastEvents = new(MockBehavior.Strict);

        // Act
        TraceHistoryEventLookup lookup = new(pastEvents.Object);

        // Assert
        lookup.Should().NotBeNull();
        pastEvents.Verify(events => events.GetEnumerator(), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetEvent_DuplicateUnqueriedId_Throws(bool taskEvents)
    {
        // Arrange
        TraceHistoryEventLookup lookup = new(
        [
            CreateSchedulingEvent(1, taskEvents),
            CreateSchedulingEvent(1, taskEvents),
            CreateSchedulingEvent(2, taskEvents),
        ]);

        // Act
        Action act = () => GetEvent(lookup, 2, taskEvents);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*1*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetEvent_DuplicateIdsInOtherSchedulingType_Throws(bool taskEvents)
    {
        // Arrange
        TraceHistoryEventLookup lookup = new(
        [
            CreateSchedulingEvent(1, taskEvents),
            CreateSchedulingEvent(2, !taskEvents),
            CreateSchedulingEvent(2, !taskEvents),
        ]);

        // Act
        Action act = () => GetEvent(lookup, 1, taskEvents);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*2*");
    }

    static P.HistoryEvent? GetEvent(TraceHistoryEventLookup lookup, int eventId, bool taskEvent)
        => taskEvent
            ? lookup.GetTaskScheduledEvent(eventId)
            : lookup.GetSubOrchestrationInstanceCreatedEvent(eventId);

    static P.HistoryEvent CreateSchedulingEvent(int eventId, bool taskEvent)
        => taskEvent
            ? new P.HistoryEvent
            {
                EventId = eventId,
                TaskScheduled = new P.TaskScheduledEvent { Name = $"Task-{eventId}" },
            }
            : new P.HistoryEvent
            {
                EventId = eventId,
                SubOrchestrationInstanceCreated = new P.SubOrchestrationInstanceCreatedEvent
                {
                    InstanceId = $"sub-{eventId}",
                    Name = $"Sub-{eventId}",
                },
            };
}
