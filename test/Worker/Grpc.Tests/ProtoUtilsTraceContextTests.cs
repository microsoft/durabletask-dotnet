// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using DurableTask.Core;
using DurableTask.Core.Command;
using DurableTask.Core.Entities.OperationFormat;
using Newtonsoft.Json;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

public class ProtoUtilsTraceContextTests
{
    static readonly ActivitySource TestSource = new(nameof(ProtoUtilsTraceContextTests));

    [Fact]
    public void SendEntityMessage_SignalEntity_SetsParentTraceContext()
    {
        // Arrange
        using ActivityListener listener = CreateListener();
        using Activity? orchestrationActivity = TestSource.StartActivity("TestOrchestration");
        orchestrationActivity.Should().NotBeNull();

        string requestId = Guid.NewGuid().ToString();
        string entityInstanceId = "@counter@myKey";
        string eventData = JsonConvert.SerializeObject(new
        {
            op = "increment",
            signal = true,
            id = requestId,
        });

        SendEventOrchestratorAction sendEventAction = new()
        {
            Id = 1,
            Instance = new OrchestrationInstance { InstanceId = entityInstanceId },
            EventName = "op",
            EventData = eventData,
        };

        ProtoUtils.EntityConversionState entityConversionState = new(insertMissingEntityUnlocks: false);

        // Act
        P.OrchestratorResponse response = ProtoUtils.ConstructOrchestratorResponse(
            instanceId: "test-orchestration",
            executionId: "exec-1",
            customStatus: null,
            actions: [sendEventAction],
            completionToken: "token",
            entityConversionState: entityConversionState,
            orchestrationActivity: orchestrationActivity);

        // Assert
        response.Actions.Should().ContainSingle();
        P.OrchestratorAction action = response.Actions[0];
        action.SendEntityMessage.Should().NotBeNull();
        action.SendEntityMessage.EntityOperationSignaled.Should().NotBeNull();
        action.SendEntityMessage.ParentTraceContext.Should().NotBeNull();
        action.SendEntityMessage.ParentTraceContext.TraceParent.Should().NotBeNullOrEmpty();
        action.SendEntityMessage.ParentTraceContext.TraceParent.Should().Contain(
            orchestrationActivity!.TraceId.ToString());
    }

    [Fact]
    public void SendEntityMessage_CallEntity_SetsParentTraceContext()
    {
        // Arrange
        using ActivityListener listener = CreateListener();
        using Activity? orchestrationActivity = TestSource.StartActivity("TestOrchestration");
        orchestrationActivity.Should().NotBeNull();

        string requestId = Guid.NewGuid().ToString();
        string entityInstanceId = "@counter@myKey";
        string eventData = JsonConvert.SerializeObject(new
        {
            op = "get",
            signal = false,
            id = requestId,
            parent = "parent-instance",
        });

        SendEventOrchestratorAction sendEventAction = new()
        {
            Id = 1,
            Instance = new OrchestrationInstance { InstanceId = entityInstanceId },
            EventName = "op",
            EventData = eventData,
        };

        ProtoUtils.EntityConversionState entityConversionState = new(insertMissingEntityUnlocks: false);

        // Act
        P.OrchestratorResponse response = ProtoUtils.ConstructOrchestratorResponse(
            instanceId: "test-orchestration",
            executionId: "exec-1",
            customStatus: null,
            actions: [sendEventAction],
            completionToken: "token",
            entityConversionState: entityConversionState,
            orchestrationActivity: orchestrationActivity);

        // Assert
        response.Actions.Should().ContainSingle();
        P.OrchestratorAction action = response.Actions[0];
        action.SendEntityMessage.Should().NotBeNull();
        action.SendEntityMessage.EntityOperationCalled.Should().NotBeNull();
        action.SendEntityMessage.ParentTraceContext.Should().NotBeNull();
        action.SendEntityMessage.ParentTraceContext.TraceParent.Should().NotBeNullOrEmpty();
        action.SendEntityMessage.ParentTraceContext.TraceParent.Should().Contain(
            orchestrationActivity!.TraceId.ToString());
    }

    [Fact]
    public void SendEntityMessage_NoOrchestrationActivity_DoesNotSetParentTraceContext()
    {
        // Arrange
        string requestId = Guid.NewGuid().ToString();
        string entityInstanceId = "@counter@myKey";
        string eventData = JsonConvert.SerializeObject(new
        {
            op = "increment",
            signal = true,
            id = requestId,
        });

        SendEventOrchestratorAction sendEventAction = new()
        {
            Id = 1,
            Instance = new OrchestrationInstance { InstanceId = entityInstanceId },
            EventName = "op",
            EventData = eventData,
        };

        ProtoUtils.EntityConversionState entityConversionState = new(insertMissingEntityUnlocks: false);

        // Act
        P.OrchestratorResponse response = ProtoUtils.ConstructOrchestratorResponse(
            instanceId: "test-orchestration",
            executionId: "exec-1",
            customStatus: null,
            actions: [sendEventAction],
            completionToken: "token",
            entityConversionState: entityConversionState,
            orchestrationActivity: null);

        // Assert
        response.Actions.Should().ContainSingle();
        P.OrchestratorAction action = response.Actions[0];
        action.SendEntityMessage.Should().NotBeNull();
        action.SendEntityMessage.ParentTraceContext.Should().BeNull();
    }

    [Fact]
    public void SendEntityMessage_NoEntityConversionState_SendsAsSendEvent()
    {
        // Arrange
        using ActivityListener listener = CreateListener();
        using Activity? orchestrationActivity = TestSource.StartActivity("TestOrchestration");

        string requestId = Guid.NewGuid().ToString();
        string entityInstanceId = "@counter@myKey";
        string eventData = JsonConvert.SerializeObject(new
        {
            op = "increment",
            signal = true,
            id = requestId,
        });

        SendEventOrchestratorAction sendEventAction = new()
        {
            Id = 1,
            Instance = new OrchestrationInstance { InstanceId = entityInstanceId },
            EventName = "op",
            EventData = eventData,
        };

        // Act - no entityConversionState means entity events are NOT converted
        P.OrchestratorResponse response = ProtoUtils.ConstructOrchestratorResponse(
            instanceId: "test-orchestration",
            executionId: "exec-1",
            customStatus: null,
            actions: [sendEventAction],
            completionToken: "token",
            entityConversionState: null,
            orchestrationActivity: orchestrationActivity);

        // Assert - should be a SendEvent, not SendEntityMessage
        response.Actions.Should().ContainSingle();
        P.OrchestratorAction action = response.Actions[0];
        action.SendEvent.Should().NotBeNull();
        action.SendEntityMessage.Should().BeNull();
    }

    [Fact]
    public void SendEntityMessage_TraceContextHasUniqueSpanId()
    {
        // Arrange
        using ActivityListener listener = CreateListener();
        using Activity? orchestrationActivity = TestSource.StartActivity("TestOrchestration");
        orchestrationActivity.Should().NotBeNull();

        string entityInstanceId = "@counter@myKey";
        string eventData1 = JsonConvert.SerializeObject(new
        {
            op = "increment",
            signal = true,
            id = Guid.NewGuid().ToString(),
        });

        string eventData2 = JsonConvert.SerializeObject(new
        {
            op = "increment",
            signal = true,
            id = Guid.NewGuid().ToString(),
        });

        SendEventOrchestratorAction action1 = new()
        {
            Id = 1,
            Instance = new OrchestrationInstance { InstanceId = entityInstanceId },
            EventName = "op",
            EventData = eventData1,
        };

        SendEventOrchestratorAction action2 = new()
        {
            Id = 2,
            Instance = new OrchestrationInstance { InstanceId = entityInstanceId },
            EventName = "op",
            EventData = eventData2,
        };

        ProtoUtils.EntityConversionState entityConversionState = new(insertMissingEntityUnlocks: false);

        // Act
        P.OrchestratorResponse response = ProtoUtils.ConstructOrchestratorResponse(
            instanceId: "test-orchestration",
            executionId: "exec-1",
            customStatus: null,
            actions: [action1, action2],
            completionToken: "token",
            entityConversionState: entityConversionState,
            orchestrationActivity: orchestrationActivity);

        // Assert - each entity message should get a unique span ID
        response.Actions.Should().HaveCount(2);
        string traceParent1 = response.Actions[0].SendEntityMessage.ParentTraceContext.TraceParent;
        string traceParent2 = response.Actions[1].SendEntityMessage.ParentTraceContext.TraceParent;
        traceParent1.Should().NotBeNullOrEmpty();
        traceParent2.Should().NotBeNullOrEmpty();

        // Same trace ID (from orchestration activity)
        traceParent1.Should().Contain(orchestrationActivity!.TraceId.ToString());
        traceParent2.Should().Contain(orchestrationActivity.TraceId.ToString());

        // Different span IDs
        traceParent1.Should().NotBe(traceParent2);
    }

    static ActivityListener CreateListener()
    {
        ActivityListener listener = new()
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };

        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    [Fact]
    public void ToEntityBatchRequest_SignalEntity_ExtractsTraceContext()
    {
        // Arrange
        string traceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";
        string traceState = "vendor=value";

        P.EntityRequest entityRequest = new()
        {
            InstanceId = "@counter@myKey",
            OperationRequests =
            {
                new P.HistoryEvent
                {
                    EntityOperationSignaled = new P.EntityOperationSignaledEvent
                    {
                        RequestId = Guid.NewGuid().ToString(),
                        Operation = "increment",
                        ParentTraceContext = new P.TraceContext
                        {
                            TraceParent = traceParent,
                            TraceState = traceState,
                        },
                    },
                },
            },
        };

        // Act
        entityRequest.ToEntityBatchRequest(out EntityBatchRequest batchRequest, out _);

        // Assert
        batchRequest.Operations.Should().ContainSingle();
        batchRequest.Operations[0].TraceContext.Should().NotBeNull();
        batchRequest.Operations[0].TraceContext!.TraceParent.Should().Be(traceParent);
        batchRequest.Operations[0].TraceContext!.TraceState.Should().Be(traceState);
    }

    [Fact]
    public void ToEntityBatchRequest_CallEntity_ExtractsTraceContext()
    {
        // Arrange
        string traceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";
        string traceState = "vendor=value";

        P.EntityRequest entityRequest = new()
        {
            InstanceId = "@counter@myKey",
            OperationRequests =
            {
                new P.HistoryEvent
                {
                    EntityOperationCalled = new P.EntityOperationCalledEvent
                    {
                        RequestId = Guid.NewGuid().ToString(),
                        Operation = "get",
                        ParentInstanceId = "parent-instance",
                        ParentExecutionId = "parent-exec",
                        ParentTraceContext = new P.TraceContext
                        {
                            TraceParent = traceParent,
                            TraceState = traceState,
                        },
                    },
                },
            },
        };

        // Act
        entityRequest.ToEntityBatchRequest(out EntityBatchRequest batchRequest, out _);

        // Assert
        batchRequest.Operations.Should().ContainSingle();
        batchRequest.Operations[0].TraceContext.Should().NotBeNull();
        batchRequest.Operations[0].TraceContext!.TraceParent.Should().Be(traceParent);
        batchRequest.Operations[0].TraceContext!.TraceState.Should().Be(traceState);
    }

    [Fact]
    public void ToEntityBatchRequest_NoTraceContext_LeavesTraceContextNull()
    {
        // Arrange
        P.EntityRequest entityRequest = new()
        {
            InstanceId = "@counter@myKey",
            OperationRequests =
            {
                new P.HistoryEvent
                {
                    EntityOperationSignaled = new P.EntityOperationSignaledEvent
                    {
                        RequestId = Guid.NewGuid().ToString(),
                        Operation = "increment",
                    },
                },
            },
        };

        // Act
        entityRequest.ToEntityBatchRequest(out EntityBatchRequest batchRequest, out _);

        // Assert
        batchRequest.Operations.Should().ContainSingle();
        batchRequest.Operations[0].TraceContext.Should().BeNull();
    }
}
