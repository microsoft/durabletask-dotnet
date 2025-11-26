// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client.Tests;

public class OrchestrationMetadataHistoryTests
{
    [Fact]
    public void History_DefaultsToNull()
    {
        // Arrange & Act
        OrchestrationMetadata metadata = new("TestOrchestration", "instance-1");

        // Assert
        metadata.History.Should().BeNull();
    }

    [Fact]
    public void History_CanBeSetViaInitializer()
    {
        // Arrange
        List<OrchestrationHistoryEvent> history = new()
        {
            new(0, "ExecutionStarted", DateTimeOffset.UtcNow),
            new(1, "TaskScheduled", DateTimeOffset.UtcNow),
            new(2, "TaskCompleted", DateTimeOffset.UtcNow),
            new(3, "ExecutionCompleted", DateTimeOffset.UtcNow)
        };

        // Act
        OrchestrationMetadata metadata = new("TestOrchestration", "instance-1")
        {
            History = history
        };

        // Assert
        metadata.History.Should().NotBeNull();
        metadata.History.Should().HaveCount(4);
        metadata.History![0].EventType.Should().Be("ExecutionStarted");
        metadata.History[3].EventType.Should().Be("ExecutionCompleted");
    }

    [Fact]
    public void History_CanBeEmptyList()
    {
        // Arrange & Act
        OrchestrationMetadata metadata = new("TestOrchestration", "instance-1")
        {
            History = new List<OrchestrationHistoryEvent>()
        };

        // Assert
        metadata.History.Should().NotBeNull();
        metadata.History.Should().BeEmpty();
    }

    [Fact]
    public void History_WithTypicalOrchestrationEvents()
    {
        // Arrange
        DateTimeOffset startTime = DateTimeOffset.UtcNow;
        List<OrchestrationHistoryEvent> history = new()
        {
            new(0, "ExecutionStarted", startTime)
            {
                Name = "HelloOrchestration",
                Input = "{\"name\":\"World\"}"
            },
            new(1, "TaskScheduled", startTime.AddMilliseconds(10))
            {
                Name = "SayHello",
                Input = "\"World\""
            },
            new(2, "TaskCompleted", startTime.AddMilliseconds(50))
            {
                ScheduledTaskId = 1,
                Result = "\"Hello, World!\""
            },
            new(3, "ExecutionCompleted", startTime.AddMilliseconds(60))
            {
                OrchestrationStatus = OrchestrationRuntimeStatus.Completed,
                Result = "\"Hello, World!\""
            }
        };

        // Act
        OrchestrationMetadata metadata = new("HelloOrchestration", "hello-instance-1")
        {
            RuntimeStatus = OrchestrationRuntimeStatus.Completed,
            CreatedAt = startTime,
            LastUpdatedAt = startTime.AddMilliseconds(60),
            History = history
        };

        // Assert
        metadata.History.Should().HaveCount(4);

        // Verify ExecutionStarted event
        OrchestrationHistoryEvent startedEvent = metadata.History![0];
        startedEvent.EventType.Should().Be("ExecutionStarted");
        startedEvent.Name.Should().Be("HelloOrchestration");
        startedEvent.Input.Should().Be("{\"name\":\"World\"}");

        // Verify TaskScheduled event
        OrchestrationHistoryEvent scheduledEvent = metadata.History[1];
        scheduledEvent.EventType.Should().Be("TaskScheduled");
        scheduledEvent.Name.Should().Be("SayHello");

        // Verify TaskCompleted event
        OrchestrationHistoryEvent completedEvent = metadata.History[2];
        completedEvent.EventType.Should().Be("TaskCompleted");
        completedEvent.ScheduledTaskId.Should().Be(1);
        completedEvent.Result.Should().Be("\"Hello, World!\"");

        // Verify ExecutionCompleted event
        OrchestrationHistoryEvent executionCompletedEvent = metadata.History[3];
        executionCompletedEvent.OrchestrationStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
    }

    [Fact]
    public void History_WithFailedOrchestration()
    {
        // Arrange
        DateTimeOffset startTime = DateTimeOffset.UtcNow;
        TaskFailureDetails failureDetails = new(
            "InvalidOperationException",
            "Something went wrong",
            "   at Test.Method()",
            null,
            null);

        List<OrchestrationHistoryEvent> history = new()
        {
            new(0, "ExecutionStarted", startTime)
            {
                Name = "FailingOrchestration"
            },
            new(1, "TaskScheduled", startTime.AddMilliseconds(10))
            {
                Name = "FailingActivity"
            },
            new(2, "TaskFailed", startTime.AddMilliseconds(50))
            {
                ScheduledTaskId = 1,
                FailureDetails = failureDetails
            },
            new(3, "ExecutionCompleted", startTime.AddMilliseconds(60))
            {
                OrchestrationStatus = OrchestrationRuntimeStatus.Failed,
                FailureDetails = failureDetails
            }
        };

        // Act
        OrchestrationMetadata metadata = new("FailingOrchestration", "failing-instance-1")
        {
            RuntimeStatus = OrchestrationRuntimeStatus.Failed,
            History = history
        };

        // Assert
        metadata.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Failed);
        metadata.History.Should().HaveCount(4);

        OrchestrationHistoryEvent taskFailedEvent = metadata.History![2];
        taskFailedEvent.EventType.Should().Be("TaskFailed");
        taskFailedEvent.FailureDetails.Should().NotBeNull();
        taskFailedEvent.FailureDetails!.ErrorType.Should().Be("InvalidOperationException");
    }

    [Fact]
    public void History_WithTimerEvents()
    {
        // Arrange
        DateTimeOffset startTime = DateTimeOffset.UtcNow;
        DateTimeOffset fireAt = startTime.AddMinutes(5);

        List<OrchestrationHistoryEvent> history = new()
        {
            new(0, "ExecutionStarted", startTime),
            new(1, "TimerCreated", startTime.AddMilliseconds(10))
            {
                FireAt = fireAt
            },
            new(2, "TimerFired", fireAt)
            {
                ScheduledTaskId = 1,
                FireAt = fireAt
            },
            new(3, "ExecutionCompleted", fireAt.AddMilliseconds(10))
        };

        // Act
        OrchestrationMetadata metadata = new("TimerOrchestration", "timer-instance-1")
        {
            History = history
        };

        // Assert
        metadata.History.Should().HaveCount(4);

        OrchestrationHistoryEvent timerCreatedEvent = metadata.History![1];
        timerCreatedEvent.EventType.Should().Be("TimerCreated");
        timerCreatedEvent.FireAt.Should().Be(fireAt);

        OrchestrationHistoryEvent timerFiredEvent = metadata.History[2];
        timerFiredEvent.EventType.Should().Be("TimerFired");
        timerFiredEvent.ScheduledTaskId.Should().Be(1);
        timerFiredEvent.FireAt.Should().Be(fireAt);
    }

    [Fact]
    public void History_WithSubOrchestrationEvents()
    {
        // Arrange
        DateTimeOffset startTime = DateTimeOffset.UtcNow;

        List<OrchestrationHistoryEvent> history = new()
        {
            new(0, "ExecutionStarted", startTime),
            new(1, "SubOrchestrationInstanceCreated", startTime.AddMilliseconds(10))
            {
                Name = "ChildOrchestration",
                InstanceId = "child-instance-1",
                Input = "{\"value\":42}"
            },
            new(2, "SubOrchestrationInstanceCompleted", startTime.AddSeconds(5))
            {
                ScheduledTaskId = 1,
                Result = "\"child-result\""
            },
            new(3, "ExecutionCompleted", startTime.AddSeconds(5).AddMilliseconds(10))
        };

        // Act
        OrchestrationMetadata metadata = new("ParentOrchestration", "parent-instance-1")
        {
            History = history
        };

        // Assert
        metadata.History.Should().HaveCount(4);

        OrchestrationHistoryEvent subOrchCreatedEvent = metadata.History![1];
        subOrchCreatedEvent.EventType.Should().Be("SubOrchestrationInstanceCreated");
        subOrchCreatedEvent.Name.Should().Be("ChildOrchestration");
        subOrchCreatedEvent.InstanceId.Should().Be("child-instance-1");

        OrchestrationHistoryEvent subOrchCompletedEvent = metadata.History[2];
        subOrchCompletedEvent.EventType.Should().Be("SubOrchestrationInstanceCompleted");
        subOrchCompletedEvent.ScheduledTaskId.Should().Be(1);
        subOrchCompletedEvent.Result.Should().Be("\"child-result\"");
    }

    [Fact]
    public void History_WithExternalEvents()
    {
        // Arrange
        DateTimeOffset startTime = DateTimeOffset.UtcNow;

        List<OrchestrationHistoryEvent> history = new()
        {
            new(0, "ExecutionStarted", startTime),
            new(1, "EventSent", startTime.AddMilliseconds(100))
            {
                Name = "ApprovalRequest",
                InstanceId = "approval-handler-1",
                Input = "{\"requestId\":\"req-123\"}"
            },
            new(2, "EventRaised", startTime.AddMinutes(5))
            {
                Name = "ApprovalResponse",
                Input = "{\"approved\":true}"
            },
            new(3, "ExecutionCompleted", startTime.AddMinutes(5).AddMilliseconds(10))
        };

        // Act
        OrchestrationMetadata metadata = new("ApprovalOrchestration", "approval-instance-1")
        {
            History = history
        };

        // Assert
        metadata.History.Should().HaveCount(4);

        OrchestrationHistoryEvent eventSentEvent = metadata.History![1];
        eventSentEvent.EventType.Should().Be("EventSent");
        eventSentEvent.Name.Should().Be("ApprovalRequest");
        eventSentEvent.InstanceId.Should().Be("approval-handler-1");

        OrchestrationHistoryEvent eventRaisedEvent = metadata.History[2];
        eventRaisedEvent.EventType.Should().Be("EventRaised");
        eventRaisedEvent.Name.Should().Be("ApprovalResponse");
        eventRaisedEvent.Input.Should().Be("{\"approved\":true}");
    }
}
