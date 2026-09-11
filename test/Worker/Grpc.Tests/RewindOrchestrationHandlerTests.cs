// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

public class RewindOrchestrationHandlerTests
{
    [Fact]
    public void CreateResponse_RewritesFailedHistory()
    {
        // Arrange
        P.HistoryEvent executionStarted = CreateExecutionStarted("old-execution");
        P.HistoryEvent[] pastEvents =
        [
            executionStarted,
            new()
            {
                EventId = 0,
                TaskScheduled = new P.TaskScheduledEvent { Name = "SuccessfulActivity" },
            },
            new()
            {
                EventId = 1,
                TaskCompleted = new P.TaskCompletedEvent { TaskScheduledId = 0 },
            },
            new()
            {
                EventId = 2,
                TaskScheduled = new P.TaskScheduledEvent { Name = "FailedActivity" },
            },
            new()
            {
                EventId = 3,
                TaskFailed = new P.TaskFailedEvent { TaskScheduledId = 2 },
            },
            new()
            {
                EventId = 4,
                SubOrchestrationInstanceCreated =
                    new P.SubOrchestrationInstanceCreatedEvent { InstanceId = "failed-child" },
            },
            new()
            {
                EventId = 5,
                SubOrchestrationInstanceFailed =
                    new P.SubOrchestrationInstanceFailedEvent { TaskScheduledId = 4 },
            },
            new()
            {
                EventId = 6,
                ExecutionCompleted = new P.ExecutionCompletedEvent
                {
                    OrchestrationStatus = P.OrchestrationStatus.Failed,
                },
            },
        ];

        // Act
        P.OrchestratorResponse response = RewindOrchestrationHandler.CreateResponse(
            CreateRewindRequest(),
            pastEvents,
            "completion-token",
            orchestrationActivity: null);

        // Assert
        P.OrchestratorAction action = response.Actions.Should().ContainSingle().Subject;
        action.Id.Should().Be(-1);
        P.HistoryEvent[] newHistory = action.RewindOrchestration.NewHistory.ToArray();

        newHistory.Should().NotContain(e =>
            e.EventTypeCase == P.HistoryEvent.EventTypeOneofCase.TaskFailed
            || e.EventTypeCase == P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceFailed
            || e.EventTypeCase == P.HistoryEvent.EventTypeOneofCase.ExecutionCompleted
            || (e.EventTypeCase == P.HistoryEvent.EventTypeOneofCase.TaskScheduled && e.EventId == 2));
        newHistory.Should().Contain(e =>
            e.EventTypeCase == P.HistoryEvent.EventTypeOneofCase.TaskScheduled && e.EventId == 0);
        newHistory.Should().Contain(e =>
            e.EventTypeCase == P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCreated
            && e.SubOrchestrationInstanceCreated.InstanceId == "failed-child");
        newHistory.Last().ExecutionRewound.Reason.Should().Be("rewind reason");

        string newExecutionId = newHistory[0].ExecutionStarted.OrchestrationInstance.ExecutionId;
        Guid.TryParseExact(newExecutionId, "N", out _).Should().BeTrue();
        executionStarted.ExecutionStarted.OrchestrationInstance.ExecutionId.Should().Be("old-execution");
    }

    [Fact]
    public void CreateResponse_UpdatesParentExecutionId()
    {
        // Arrange
        P.HistoryEvent executionStarted = CreateExecutionStarted(
            "old-execution",
            parentExecutionId: "old-parent-execution");
        P.OrchestratorRequest request = CreateRewindRequest(parentExecutionId: "new-parent-execution");

        // Act
        P.OrchestratorResponse response = RewindOrchestrationHandler.CreateResponse(
            request,
            [executionStarted],
            "completion-token",
            orchestrationActivity: null);

        // Assert
        P.ExecutionStartedEvent rewrittenStart =
            response.Actions[0].RewindOrchestration.NewHistory[0].ExecutionStarted;
        rewrittenStart.ParentInstance.OrchestrationInstance.ExecutionId.Should().Be("new-parent-execution");
        executionStarted.ExecutionStarted.ParentInstance.OrchestrationInstance.ExecutionId
            .Should().Be("old-parent-execution");
    }

    [Fact]
    public void CreateResponse_UpdatesParentTraceContext()
    {
        // Arrange
        P.TraceContext originalParentTraceContext = CreateTraceContext(
            "11111111111111111111111111111111",
            "2222222222222222");
        P.TraceContext rewindParentTraceContext = CreateTraceContext(
            "33333333333333333333333333333333",
            "4444444444444444",
            "vendor=value");
        P.HistoryEvent executionStarted = CreateExecutionStarted(
            "old-execution",
            parentExecutionId: "old-parent-execution",
            parentTraceContext: originalParentTraceContext);
        P.OrchestratorRequest request = CreateRewindRequest(
            parentExecutionId: "new-parent-execution",
            parentTraceContext: rewindParentTraceContext);

        // Act
        P.OrchestratorResponse response = RewindOrchestrationHandler.CreateResponse(
            request,
            [executionStarted],
            "completion-token",
            orchestrationActivity: null);

        // Assert
        P.ExecutionStartedEvent rewrittenStart =
            response.Actions[0].RewindOrchestration.NewHistory[0].ExecutionStarted;
        rewrittenStart.ParentTraceContext.TraceParent.Should().Be(rewindParentTraceContext.TraceParent);
        rewrittenStart.ParentTraceContext.TraceState.Should().Be(rewindParentTraceContext.TraceState);

        // The original trace context should not be modified
        executionStarted.ExecutionStarted.ParentTraceContext.TraceParent
            .Should().Be(originalParentTraceContext.TraceParent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateResponse_ReplacesFailedSubOrchestrationTraceContext(
        bool rewindHasParentTraceContext)
    {
        // Arrange
        const string ExecutionTraceId = "11111111111111111111111111111111";
        const string ExecutionParentSpanId = "2222222222222222";
        const string ExecutionTraceState = "execution=value";
        const string OldChildSpanId = "3333333333333333";
        const string RewindTraceId = "44444444444444444444444444444444";
        const string RewindParentSpanId = "5555555555555555";
        const string RewindTraceState = "rewind=value";

        P.HistoryEvent failedChildCreated = new()
        {
            EventId = 4,
            SubOrchestrationInstanceCreated = new P.SubOrchestrationInstanceCreatedEvent
            {
                InstanceId = "failed-child",
                ParentTraceContext = CreateTraceContext(ExecutionTraceId, OldChildSpanId),
            },
        };
        P.HistoryEvent[] pastEvents =
        [
            CreateExecutionStarted(
                "old-execution",
                parentTraceContext: CreateTraceContext(
                    ExecutionTraceId,
                    ExecutionParentSpanId,
                    ExecutionTraceState)),
            failedChildCreated,
            new()
            {
                EventId = 5,
                SubOrchestrationInstanceFailed =
                    new P.SubOrchestrationInstanceFailedEvent { TaskScheduledId = 4 },
            },
        ];
        P.TraceContext? rewindParentTraceContext = rewindHasParentTraceContext
            ? CreateTraceContext(RewindTraceId, RewindParentSpanId, RewindTraceState)
            : null;

        // Act
        P.OrchestratorResponse response = RewindOrchestrationHandler.CreateResponse(
            CreateRewindRequest(parentTraceContext: rewindParentTraceContext),
            pastEvents,
            "completion-token",
            orchestrationActivity: null);

        // Assert
        P.SubOrchestrationInstanceCreatedEvent rewrittenChild = response
            .Actions[0]
            .RewindOrchestration
            .NewHistory
            .Single(e => e.EventId == 4)
            .SubOrchestrationInstanceCreated;
        ActivityContext.TryParse(
            rewrittenChild.ParentTraceContext.TraceParent,
            rewrittenChild.ParentTraceContext.TraceState,
            out ActivityContext childTraceContext).Should().BeTrue();

        string expectedTraceId = rewindHasParentTraceContext ? RewindTraceId : ExecutionTraceId;
        childTraceContext.TraceId.Should().Be(ActivityTraceId.CreateFromString(expectedTraceId.AsSpan()));

        // A new span ID should be generated
        childTraceContext.SpanId.ToString().Should().NotBe(OldChildSpanId);
        childTraceContext.SpanId.ToString().Should().NotBe(ExecutionParentSpanId);
        childTraceContext.SpanId.ToString().Should().NotBe(RewindParentSpanId);

        childTraceContext.TraceFlags.Should().Be(ActivityTraceFlags.Recorded);
        childTraceContext.TraceState.Should().Be(
            rewindHasParentTraceContext ? RewindTraceState : ExecutionTraceState);

        // The original trace context should not be modified
        failedChildCreated.SubOrchestrationInstanceCreated.ParentTraceContext.TraceParent
            .Should().Be(CreateTraceContext(ExecutionTraceId, OldChildSpanId).TraceParent);
    }

    [Fact]
    public void CreateResponse_RejectsUnexpectedNewEvents()
    {
        // Arrange
        P.OrchestratorRequest request = new()
        {
            NewEvents =
            {
                new P.HistoryEvent
                {
                    ExecutionRewound = new P.ExecutionRewoundEvent(),
                },
            },
        };

        // Act
        Action act = () => RewindOrchestrationHandler.CreateResponse(
            request,
            [],
            "completion-token",
            orchestrationActivity: null);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*exactly two events*");
    }

    static P.HistoryEvent CreateExecutionStarted(
        string executionId,
        string? parentExecutionId = null,
        P.TraceContext? parentTraceContext = null)
    {
        P.ExecutionStartedEvent executionStarted = new()
        {
            Name = "TestOrchestration",
            Input = "input",
            ParentTraceContext = parentTraceContext,
            OrchestrationInstance = new P.OrchestrationInstance
            {
                InstanceId = "instance",
                ExecutionId = executionId,
            },
        };

        if (parentExecutionId is not null)
        {
            executionStarted.ParentInstance = new P.ParentInstanceInfo
            {
                OrchestrationInstance = new P.OrchestrationInstance
                {
                    InstanceId = "parent",
                    ExecutionId = parentExecutionId,
                },
            };
        }

        return new P.HistoryEvent
        {
            EventId = -1,
            ExecutionStarted = executionStarted,
        };
    }

    static P.OrchestratorRequest CreateRewindRequest(
        string? parentExecutionId = null,
        P.TraceContext? parentTraceContext = null)
    {
        return new P.OrchestratorRequest
        {
            InstanceId = "instance",
            NewEvents =
            {
                new P.HistoryEvent
                {
                    OrchestratorStarted = new P.OrchestratorStartedEvent(),
                },
                new P.HistoryEvent
                {
                    ExecutionRewound = new P.ExecutionRewoundEvent
                    {
                        Reason = "rewind reason",
                        ParentExecutionId = parentExecutionId,
                        ParentTraceContext = parentTraceContext,
                    },
                },
            },
        };
    }

    static P.TraceContext CreateTraceContext(string traceId, string spanId, string? traceState = null)
    {
        return new()
        {
            TraceParent = $"00-{traceId}-{spanId}-01",
            TraceState = traceState,
        };
    }
}
