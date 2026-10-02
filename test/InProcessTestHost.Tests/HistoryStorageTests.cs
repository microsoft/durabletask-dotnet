// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.History;
using DurableTask.Core.Serializing;
using Grpc.Core;
using Microsoft.DurableTask.Testing.Sidecar;
using Microsoft.DurableTask.Testing.Sidecar.Grpc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;
using P = Microsoft.DurableTask.Protobuf;

namespace InProcessTestHost.Tests;

/// <summary>
/// Tests committed-history storage and the test sidecar's history stream.
/// </summary>
public class HistoryStorageTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("current")]
    public async Task GetHistoryAsync_PendingInstance_ReturnsEmptyHistory(string? executionId)
    {
        // Arrange
        InMemoryOrchestrationService service = new();
        ExecutionStartedEvent started = CreateStartedEvent("current");
        await service.CreateTaskOrchestrationAsync(new TaskMessage
        {
            OrchestrationInstance = started.OrchestrationInstance,
            Event = started,
        });

        // Act
        string json = await service.GetOrchestrationHistoryAsync("instance", executionId);

        // Assert
        Assert.Empty(JsonDataConverter.Default.Deserialize<List<HistoryEvent>>(json));
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "previous")]
    public async Task GetHistoryAsync_MissingInstanceOrExecution_ReturnsNull(bool exists, string? executionId)
    {
        // Arrange
        InMemoryOrchestrationService service = new();
        if (exists)
        {
            await SaveAsync(service, CreateStartedEvent("current"));
        }

        // Act
        string? json = await service.GetOrchestrationHistoryAsync("instance", executionId);

        // Assert
        Assert.Null(json);
    }

    [Fact]
    public async Task GetHistoryAsync_UncommittedEvents_AreExcludedFromSnapshot()
    {
        // Arrange
        InMemoryOrchestrationService service = new();
        ExecutionStartedEvent started = CreateStartedEvent("current");
        await SaveAsync(service, started);
        await service.SendTaskOrchestrationMessageAsync(new TaskMessage
        {
            OrchestrationInstance = started.OrchestrationInstance,
            Event = new EventRaisedEvent(-1, "not committed") { Name = "Message" },
        });

        // Act
        string json = await service.GetOrchestrationHistoryAsync("instance", "current");

        // Assert
        ExecutionStartedEvent snapshot = Assert.IsType<ExecutionStartedEvent>(
            Assert.Single(JsonDataConverter.Default.Deserialize<List<HistoryEvent>>(json)));
        Assert.Equal("current", snapshot.OrchestrationInstance.ExecutionId);
        Assert.Equal(started.Timestamp, snapshot.Timestamp);
    }

    [Fact]
    public async Task GetHistoryAsync_NewGeneration_ReplacesHistoryWithoutChangingPreviousSnapshot()
    {
        // Arrange
        InMemoryOrchestrationService service = new();
        await SaveAsync(service, CreateStartedEvent("previous"));
        string previous = await service.GetOrchestrationHistoryAsync("instance", "previous");
        await SaveAsync(service, CreateStartedEvent("current"));

        // Act
        string current = await service.GetOrchestrationHistoryAsync("instance", null);
        string? oldExecution = await service.GetOrchestrationHistoryAsync("instance", "previous");

        // Assert
        Assert.Null(oldExecution);
        Assert.Equal("previous", Assert.IsType<ExecutionStartedEvent>(
            Assert.Single(JsonDataConverter.Default.Deserialize<List<HistoryEvent>>(previous))).OrchestrationInstance.ExecutionId);
        Assert.Equal("current", Assert.IsType<ExecutionStartedEvent>(
            Assert.Single(JsonDataConverter.Default.Deserialize<List<HistoryEvent>>(current))).OrchestrationInstance.ExecutionId);
    }

    [Theory]
    [InlineData(false, null, false)]
    [InlineData(true, "previous", false)]
    [InlineData(false, null, true)]
    [InlineData(true, null, true)]
    public async Task StreamHistoryAsync_UnavailableHistory_UsesRequestSemantics(
        bool exists, string? executionId, bool forWorkItemProcessing)
    {
        // Arrange
        InMemoryOrchestrationService service = new();
        if (exists)
        {
            await SaveAsync(service, CreateStartedEvent("current"));
        }

        using TaskHubGrpcServer server = CreateServer(service);
        Mock<IServerStreamWriter<P.HistoryChunk>> writer = new(MockBehavior.Strict);
        P.StreamInstanceHistoryRequest request = new()
        {
            InstanceId = "instance",
            ExecutionId = executionId,
            ForWorkItemProcessing = forWorkItemProcessing,
        };

        // Act
        Task stream = server.StreamInstanceHistory(request, writer.Object, CreateContext());

        // Assert
        if (forWorkItemProcessing)
        {
            await stream;
        }
        else
        {
            RpcException exception = await Assert.ThrowsAsync<RpcException>(() => stream);
            Assert.Equal(StatusCode.NotFound, exception.StatusCode);
        }

        writer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StreamHistoryAsync_PendingInstance_WritesNoChunks()
    {
        // Arrange
        InMemoryOrchestrationService service = new();
        ExecutionStartedEvent started = CreateStartedEvent("current");
        await service.CreateTaskOrchestrationAsync(new TaskMessage
        {
            OrchestrationInstance = started.OrchestrationInstance,
            Event = started,
        });
        using TaskHubGrpcServer server = CreateServer(service);
        Mock<IServerStreamWriter<P.HistoryChunk>> writer = new(MockBehavior.Strict);

        // Act
        await server.StreamInstanceHistory(
            new() { InstanceId = "instance", ExecutionId = "current" }, writer.Object, CreateContext());

        // Assert
        writer.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StreamHistoryAsync_MultipleChunks_PreservesSnapshotAndHonorsCancellation(bool cancel)
    {
        // Arrange
        InMemoryOrchestrationService service = new();
        HistoryEvent[] events =
        [
            CreateStartedEvent("current"),
            new EventRaisedEvent(1, new string('x', 200 * 1024)) { Name = "First" },
            new EventRaisedEvent(2, new string('y', 200 * 1024)) { Name = "Second" },
        ];
        await SaveAsync(service, events);
        using TaskHubGrpcServer server = CreateServer(service);
        using CancellationTokenSource cancellation = new();
        List<P.HistoryChunk> chunks = new();
        Mock<IServerStreamWriter<P.HistoryChunk>> writer = new();
        writer.Setup(w => w.WriteAsync(It.IsAny<P.HistoryChunk>())).Returns<P.HistoryChunk>(async chunk =>
        {
            chunks.Add(chunk.Clone());
            await SaveAsync(service, CreateStartedEvent("next"));
            if (cancel)
            {
                cancellation.Cancel();
            }
        });

        // Act
        Task stream = server.StreamInstanceHistory(
            new() { InstanceId = "instance", ExecutionId = "current" }, writer.Object, CreateContext(cancellation.Token));

        // Assert
        if (cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream);
            Assert.Single(chunks);
        }
        else
        {
            await stream;
            Assert.Equal(2, chunks.Count);
            Assert.Equal(events.Select(ProtobufUtils.ToHistoryEventProto), chunks.SelectMany(chunk => chunk.Events));
        }
    }

    static ExecutionStartedEvent CreateStartedEvent(string executionId) => new(-1, "input")
    {
        Name = "Test",
        Version = string.Empty,
        OrchestrationInstance = new() { InstanceId = "instance", ExecutionId = executionId },
    };

    static Task SaveAsync(InMemoryOrchestrationService service, params HistoryEvent[] events)
    {
        OrchestrationRuntimeState runtimeState = new();
        foreach (HistoryEvent historyEvent in events)
        {
            runtimeState.AddEvent(historyEvent);
        }

        return service.CompleteTaskOrchestrationWorkItemAsync(
            new TaskOrchestrationWorkItem(), runtimeState, [], [], [], null!,
            new OrchestrationState
            {
                OrchestrationInstance = runtimeState.OrchestrationInstance,
                OrchestrationStatus = runtimeState.OrchestrationStatus,
            });
    }

    static TaskHubGrpcServer CreateServer(InMemoryOrchestrationService service) => new(
        Mock.Of<IHostApplicationLifetime>(), NullLoggerFactory.Instance, service, service,
        Options.Create(new TaskHubGrpcServerOptions()));

    static ServerCallContext CreateContext(CancellationToken cancellation = default)
    {
        Mock<ServerCallContext> context = new();
        context.Protected().SetupGet<CancellationToken>("CancellationTokenCore").Returns(cancellation);
        return context.Object;
    }
}
