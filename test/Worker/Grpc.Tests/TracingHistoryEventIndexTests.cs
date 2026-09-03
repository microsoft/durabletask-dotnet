// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

public class TracingHistoryEventIndexTests
{
    [Fact]
    public void GetSubOrchestrationInstanceCreatedEvent_DuplicateIds_ReturnsFirstEvent()
    {
        P.HistoryEvent first = new()
        {
            EventId = 7,
            SubOrchestrationInstanceCreated = new P.SubOrchestrationInstanceCreatedEvent { Name = "first" },
        };
        P.HistoryEvent second = new()
        {
            EventId = 7,
            SubOrchestrationInstanceCreated = new P.SubOrchestrationInstanceCreatedEvent { Name = "second" },
        };

        TracingHistoryEventIndex index = new([first, second]);

        index.GetSubOrchestrationInstanceCreatedEvent(7).Should().BeSameAs(first);
    }

    [Fact]
    public void GetTaskScheduledEvent_DuplicateIds_ReturnsLastEvent()
    {
        P.HistoryEvent first = new()
        {
            EventId = 11,
            TaskScheduled = new P.TaskScheduledEvent { Name = "first" },
        };
        P.HistoryEvent second = new()
        {
            EventId = 11,
            TaskScheduled = new P.TaskScheduledEvent { Name = "second" },
        };

        TracingHistoryEventIndex index = new([first, second]);

        index.GetTaskScheduledEvent(11).Should().BeSameAs(second);
    }

    [Fact]
    public void Lookups_MissingIds_ReturnNull()
    {
        P.HistoryEvent unrelated = new()
        {
            EventId = 3,
            TimerCreated = new P.TimerCreatedEvent(),
        };

        TracingHistoryEventIndex index = new([unrelated]);

        index.GetSubOrchestrationInstanceCreatedEvent(3).Should().BeNull();
        index.GetTaskScheduledEvent(3).Should().BeNull();
    }
}
