// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client.Tests;

public class OrchestrationHistoryEventTests
{
    [Fact]
    public void Constructor_SetsRequiredProperties()
    {
        // Arrange
        int eventId = 1;
        string eventType = "TaskCompleted";
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;

        // Act
        OrchestrationHistoryEvent historyEvent = new(eventId, eventType, timestamp);

        // Assert
        historyEvent.EventId.Should().Be(eventId);
        historyEvent.EventType.Should().Be(eventType);
        historyEvent.Timestamp.Should().Be(timestamp);
    }

    [Fact]
    public void OptionalProperties_DefaultToNull()
    {
        // Arrange & Act
        OrchestrationHistoryEvent historyEvent = new(1, "ExecutionStarted", DateTimeOffset.UtcNow);

        // Assert
        historyEvent.Name.Should().BeNull();
        historyEvent.Input.Should().BeNull();
        historyEvent.Result.Should().BeNull();
        historyEvent.ScheduledTaskId.Should().BeNull();
        historyEvent.InstanceId.Should().BeNull();
        historyEvent.FireAt.Should().BeNull();
        historyEvent.OrchestrationStatus.Should().BeNull();
        historyEvent.FailureDetails.Should().BeNull();
    }

    [Fact]
    public void WithExpression_CreatesNewInstanceWithModifiedProperty()
    {
        // Arrange
        OrchestrationHistoryEvent original = new(1, "TaskScheduled", DateTimeOffset.UtcNow);

        // Act
        OrchestrationHistoryEvent modified = original with { Name = "SayHello" };

        // Assert
        modified.Name.Should().Be("SayHello");
        modified.EventId.Should().Be(original.EventId);
        modified.EventType.Should().Be(original.EventType);
        modified.Timestamp.Should().Be(original.Timestamp);
    }

    [Fact]
    public void WithExpression_CanSetMultipleProperties()
    {
        // Arrange
        OrchestrationHistoryEvent original = new(1, "TaskCompleted", DateTimeOffset.UtcNow);

        // Act
        OrchestrationHistoryEvent modified = original with
        {
            ScheduledTaskId = 5,
            Result = "\"Hello, World!\""
        };

        // Assert
        modified.ScheduledTaskId.Should().Be(5);
        modified.Result.Should().Be("\"Hello, World!\"");
        modified.EventId.Should().Be(original.EventId);
    }

    [Fact]
    public void ExecutionStartedEvent_CanHaveNameInputAndInstanceId()
    {
        // Arrange & Act
        OrchestrationHistoryEvent historyEvent = new(0, "ExecutionStarted", DateTimeOffset.UtcNow)
        {
            Name = "MyOrchestration",
            Input = "{\"greeting\":\"Hello\"}",
            InstanceId = "instance-123"
        };

        // Assert
        historyEvent.Name.Should().Be("MyOrchestration");
        historyEvent.Input.Should().Be("{\"greeting\":\"Hello\"}");
        historyEvent.InstanceId.Should().Be("instance-123");
    }

    [Fact]
    public void ExecutionCompletedEvent_CanHaveOrchestrationStatusAndResult()
    {
        // Arrange & Act
        OrchestrationHistoryEvent historyEvent = new(10, "ExecutionCompleted", DateTimeOffset.UtcNow)
        {
            OrchestrationStatus = OrchestrationRuntimeStatus.Completed,
            Result = "\"Success!\""
        };

        // Assert
        historyEvent.OrchestrationStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
        historyEvent.Result.Should().Be("\"Success!\"");
    }

    [Fact]
    public void TaskFailedEvent_CanHaveFailureDetails()
    {
        // Arrange
        TaskFailureDetails failureDetails = new("InvalidOperationException", "Something went wrong", null, null, null);

        // Act
        OrchestrationHistoryEvent historyEvent = new(5, "TaskFailed", DateTimeOffset.UtcNow)
        {
            ScheduledTaskId = 3,
            FailureDetails = failureDetails
        };

        // Assert
        historyEvent.ScheduledTaskId.Should().Be(3);
        historyEvent.FailureDetails.Should().NotBeNull();
        historyEvent.FailureDetails!.ErrorType.Should().Be("InvalidOperationException");
        historyEvent.FailureDetails.ErrorMessage.Should().Be("Something went wrong");
    }

    [Fact]
    public void TimerFiredEvent_CanHaveFireAt()
    {
        // Arrange
        DateTimeOffset fireAt = DateTimeOffset.UtcNow.AddMinutes(5);

        // Act
        OrchestrationHistoryEvent historyEvent = new(8, "TimerFired", DateTimeOffset.UtcNow)
        {
            ScheduledTaskId = 7,
            FireAt = fireAt
        };

        // Assert
        historyEvent.ScheduledTaskId.Should().Be(7);
        historyEvent.FireAt.Should().Be(fireAt);
    }

    [Fact]
    public void RecordEquality_WorksCorrectly()
    {
        // Arrange
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        OrchestrationHistoryEvent event1 = new(1, "TaskScheduled", timestamp) { Name = "SayHello" };
        OrchestrationHistoryEvent event2 = new(1, "TaskScheduled", timestamp) { Name = "SayHello" };
        OrchestrationHistoryEvent event3 = new(1, "TaskScheduled", timestamp) { Name = "SayGoodbye" };

        // Assert
        event1.Should().Be(event2);
        event1.Should().NotBe(event3);
    }
}
