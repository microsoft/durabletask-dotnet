// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core.Entities.OperationFormat;
using DurableTask.Core.Tracing;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

public class ProtoUtilsEntityActionTests
{
    static readonly DateTime ScheduledTime = new(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    static readonly DateTimeOffset RequestTime = new(2029, 1, 2, 3, 4, 5, TimeSpan.Zero);
    const string TraceParent = "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01";
    const string TraceState = "vendor=value";

    [Fact]
    public void ToOperationAction_CoreStart_PreservesTagsAndExistingFields()
    {
        // Arrange
        Dictionary<string, string> tags = new()
        {
            ["owner"] = "entity",
            ["\u6807\u7b7e"] = "\u4f60\u597d \ud83c\udf0d",
            ["empty"] = string.Empty,
            [string.Empty] = "empty-key",
        };
        StartNewOrchestrationOperationAction core = new()
        {
            InstanceId = "child",
            Name = "Child",
            Version = "v2",
            Input = "\"input\"",
            ScheduledStartTime = ScheduledTime,
            RequestTime = RequestTime,
            ParentTraceContext = new DistributedTraceContext(TraceParent, TraceState),
            Tags = tags,
        };

        // Act
        P.OperationAction converted = core.ToOperationAction();
        tags.Clear();
        P.StartNewOrchestrationAction wire = P.OperationAction.Parser.ParseFrom(converted.ToByteArray()).StartNewOrchestration;

        // Assert
        wire.Tags.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["owner"] = "entity",
            ["\u6807\u7b7e"] = "\u4f60\u597d \ud83c\udf0d",
            ["empty"] = string.Empty,
            [string.Empty] = "empty-key",
        });
        Assert.Equal("child", wire.InstanceId);
        Assert.Equal("Child", wire.Name);
        Assert.Equal("v2", wire.Version);
        Assert.Equal("\"input\"", wire.Input);
        Assert.Equal(ScheduledTime, wire.ScheduledTime.ToDateTime());
        Assert.Equal(RequestTime, wire.RequestTime.ToDateTimeOffset());
        Assert.Equal(TraceParent, wire.ParentTraceContext.TraceParent);
        Assert.Equal(TraceState, wire.ParentTraceContext.TraceState);
    }

    [Fact]
    public void ToOperationAction_ProtoStart_PreservesTagsAndExistingFields()
    {
        // Arrange
        P.OperationAction proto = new()
        {
            StartNewOrchestration = new P.StartNewOrchestrationAction
            {
                InstanceId = "child",
                Name = "Child",
                Version = "v2",
                Input = "\"input\"",
                ScheduledTime = Timestamp.FromDateTime(ScheduledTime),
                RequestTime = Timestamp.FromDateTimeOffset(RequestTime),
                ParentTraceContext = new P.TraceContext { TraceParent = TraceParent, TraceState = TraceState },
                Tags = { ["owner"] = "entity", ["\u6807\u7b7e"] = "\u4f60\u597d \ud83c\udf0d", ["empty"] = string.Empty, [string.Empty] = "empty-key" },
            },
        };
        P.OperationAction wire = P.OperationAction.Parser.ParseFrom(proto.ToByteArray());

        // Act
        StartNewOrchestrationOperationAction core = Assert.IsType<StartNewOrchestrationOperationAction>(wire.ToOperationAction());
        wire.StartNewOrchestration.Tags.Clear();

        // Assert
        core.Tags.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["owner"] = "entity",
            ["\u6807\u7b7e"] = "\u4f60\u597d \ud83c\udf0d",
            ["empty"] = string.Empty,
            [string.Empty] = "empty-key",
        });
        Assert.Equal("child", core.InstanceId);
        Assert.Equal("Child", core.Name);
        Assert.Equal("v2", core.Version);
        Assert.Equal("\"input\"", core.Input);
        Assert.Equal(ScheduledTime, core.ScheduledStartTime);
        Assert.Equal(RequestTime, core.RequestTime);
        Assert.Equal(TraceParent, core.ParentTraceContext!.TraceParent);
        Assert.Equal(TraceState, core.ParentTraceContext.TraceState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ToOperationAction_NullOrEmptyTags_RoundTripsWithoutTags(bool empty)
    {
        // Arrange
        StartNewOrchestrationOperationAction original = new()
        {
            InstanceId = "child",
            Name = "Child",
            Tags = empty ? new Dictionary<string, string>() : null,
        };

        // Act
        P.OperationAction proto = original.ToOperationAction();
        StartNewOrchestrationOperationAction restored = Assert.IsType<StartNewOrchestrationOperationAction>(
            P.OperationAction.Parser.ParseFrom(proto.ToByteArray()).ToOperationAction());

        // Assert
        Assert.Empty(proto.StartNewOrchestration.Tags);
        Assert.Null(restored.Tags);
        Assert.Null(restored.Input);
        Assert.Null(restored.Version);
        Assert.Null(restored.ScheduledStartTime);
        Assert.Null(restored.RequestTime);
        Assert.Null(restored.ParentTraceContext);
    }

    [Fact]
    public void StartNewOrchestrationAction_Tags_UsesCanonicalOptionalMapField()
    {
        // Arrange
        Google.Protobuf.Reflection.MessageDescriptor descriptor = P.StartNewOrchestrationAction.Descriptor;

        // Act
        Google.Protobuf.Reflection.FieldDescriptor field = descriptor.FindFieldByName("tags");

        // Assert
        Assert.Equal(8, field.FieldNumber);
        Assert.True(field.IsMap);
        Assert.Equal(Google.Protobuf.Reflection.FieldType.String, field.MessageType.FindFieldByNumber(1).FieldType);
        Assert.Equal(Google.Protobuf.Reflection.FieldType.String, field.MessageType.FindFieldByNumber(2).FieldType);
        Assert.Empty(new P.StartNewOrchestrationAction().ToByteArray());
    }
}
