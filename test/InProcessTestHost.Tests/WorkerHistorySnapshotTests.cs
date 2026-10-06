// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Channels;
using DurableTask.Core;
using DurableTask.Core.History;
using Grpc.Core;
using Microsoft.DurableTask.Testing;
using Microsoft.DurableTask.Testing.Sidecar;
using Microsoft.DurableTask.Testing.Sidecar.Dispatcher;
using Microsoft.DurableTask.Testing.Sidecar.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;
using P = Microsoft.DurableTask.Protobuf;

namespace InProcessTestHost.Tests;

/// <summary>
/// Tests the lifetime of replay snapshots owned by dispatched orchestration episodes.
/// </summary>
public class WorkerHistorySnapshotTests
{
    /// <summary>
    /// Keeps streamed history available through reads and partial responses, but not after the final response.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteOrchestrator_FinalResponse_ReleasesSnapshot(bool partialResponse)
    {
        // Arrange
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        using TaskHubGrpcServer server = CreateServer(timeout.Token);
        Channel<P.WorkItem> workItems = Channel.CreateUnbounded<P.WorkItem>();
        Mock<IServerStreamWriter<P.WorkItem>> writer = CreateWorkItemWriter(workItems);
        Task connection = server.GetWorkItems(
            new() { Capabilities = { P.WorkerCapability.HistoryStreaming } },
            writer.Object, CreateContext(timeout.Token));
        OrchestrationInstance instance = new() { InstanceId = "instance", ExecutionId = "current" };
        HistoryEvent[] history = CreateHistory(instance);
        Task<GrpcOrchestratorExecutionResult> episode = ((ITaskExecutor)server).ExecuteOrchestrator(instance, history, []);

        try
        {
            P.OrchestratorRequest request = (await workItems.Reader.ReadAsync(timeout.Token)).OrchestratorRequest;
            Assert.True(request.RequiresHistoryStreaming);
            Assert.Empty(request.PastEvents);
            Assert.Equal(history.Select(ProtobufUtils.ToHistoryEventProto),
                await ReadWorkerHistoryAsync(server, instance.InstanceId));

            if (partialResponse)
            {
#pragma warning disable CS0612 // Exercise the legacy chunked-completion path.
                await server.CompleteOrchestratorTask(new()
                {
                    InstanceId = instance.InstanceId,
                    IsPartial = true,
                    Actions = { new P.OrchestratorAction { Id = 0, ScheduleTask = new() { Name = "First" } } },
                }, CreateContext());
#pragma warning restore CS0612
                Assert.Equal(history.Select(ProtobufUtils.ToHistoryEventProto),
                    await ReadWorkerHistoryAsync(server, instance.InstanceId));
            }

            Assert.False(episode.IsCompleted);
            Assert.Equal(1, WorkerHistorySnapshotTestHelpers.GetSnapshots(server).Count);

            // Act
            await server.CompleteOrchestratorTask(new()
            {
                InstanceId = instance.InstanceId,
                CustomStatus = "\"waiting for activity\"",
                Actions = { new P.OrchestratorAction { Id = 1, ScheduleTask = new() { Name = "Next" } } },
            }, CreateContext());
            GrpcOrchestratorExecutionResult result = await episode.WaitAsync(timeout.Token);

            // Assert
            Assert.Equal(partialResponse ? 2 : 1, result.Actions.Count());
            Assert.Equal("\"waiting for activity\"", result.CustomStatus);
            Assert.Equal(0, WorkerHistorySnapshotTestHelpers.GetSnapshots(server).Count);
            Assert.Empty(await ReadWorkerHistoryAsync(server, instance.InstanceId));
        }
        finally
        {
            timeout.Cancel();
            await connection;
        }
    }

    /// <summary>
    /// Releases a failed dispatch's snapshot without removing another active episode's history.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteOrchestrator_SendFailure_ReleasesOnlyFailedSnapshot(bool streamClosed)
    {
        // Arrange
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        using TaskHubGrpcServer server = CreateServer(timeout.Token);
        Channel<P.WorkItem> workItems = Channel.CreateUnbounded<P.WorkItem>();
        Mock<IServerStreamWriter<P.WorkItem>> writer = new();
        writer.Setup(w => w.WriteAsync(It.IsAny<P.WorkItem>())).Returns<P.WorkItem>(workItem =>
            workItem.OrchestratorRequest.InstanceId == "failed"
                ? Task.FromException(new InvalidOperationException(
                    streamClosed ? "The request is complete." : "Expected dispatch failure"))
                : workItems.Writer.WriteAsync(workItem).AsTask());
        Task connection = server.GetWorkItems(
            new() { Capabilities = { P.WorkerCapability.HistoryStreaming } },
            writer.Object, CreateContext(timeout.Token));
        ITaskExecutor executor = server;
        OrchestrationInstance other = new() { InstanceId = "other", ExecutionId = "current" };
        HistoryEvent[] otherHistory = CreateHistory(other);
        Task<GrpcOrchestratorExecutionResult> otherEpisode = executor.ExecuteOrchestrator(other, otherHistory, []);
        await workItems.Reader.ReadAsync(timeout.Token);
        OrchestrationInstance failed = new() { InstanceId = "failed", ExecutionId = "current" };

        try
        {
            // Act
            Task<GrpcOrchestratorExecutionResult> failedEpisode = executor.ExecuteOrchestrator(failed, CreateHistory(failed), []);

            // Assert
            if (streamClosed)
            {
                await Assert.ThrowsAsync<OperationCanceledException>(() => failedEpisode);
                RpcException disconnected = await Assert.ThrowsAsync<RpcException>(() =>
                    executor.ExecuteOrchestrator(failed, CreateHistory(failed), []));
                Assert.Equal(StatusCode.Unavailable, disconnected.StatusCode);
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => failedEpisode);
            }

            ConcurrentDictionary<string, List<P.HistoryEvent>> snapshots = WorkerHistorySnapshotTestHelpers.GetSnapshots(server);
            Assert.Equal(1, snapshots.Count);
            Assert.True(snapshots.ContainsKey(other.InstanceId));
            Assert.Empty(await ReadWorkerHistoryAsync(server, failed.InstanceId));
            Assert.Equal(otherHistory.Select(ProtobufUtils.ToHistoryEventProto),
                await ReadWorkerHistoryAsync(server, other.InstanceId));
            RpcException missing = await Assert.ThrowsAsync<RpcException>(() =>
                server.CompleteOrchestratorTask(new() { InstanceId = failed.InstanceId }, CreateContext()));
            Assert.Equal(StatusCode.NotFound, missing.StatusCode);
            Assert.False(otherEpisode.IsCompleted);

            await server.CompleteOrchestratorTask(new() { InstanceId = other.InstanceId }, CreateContext());
            await otherEpisode.WaitAsync(timeout.Token);
            Assert.Equal(0, snapshots.Count);
        }
        finally
        {
            timeout.Cancel();
            await connection;
        }
    }

    /// <summary>
    /// Lets a captured reader finish while a subsequent episode owns a different replay snapshot.
    /// </summary>
    [Fact]
    public async Task StreamHistoryAsync_EpisodeCompletion_PreservesReaderAndNextSnapshot()
    {
        // Arrange
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        using TaskHubGrpcServer server = CreateServer(timeout.Token);
        Channel<P.WorkItem> workItems = Channel.CreateUnbounded<P.WorkItem>();
        Mock<IServerStreamWriter<P.WorkItem>> writer = CreateWorkItemWriter(workItems);
        Task connection = server.GetWorkItems(
            new() { Capabilities = { P.WorkerCapability.HistoryStreaming } },
            writer.Object, CreateContext(timeout.Token));
        ITaskExecutor executor = server;
        OrchestrationInstance instance = new() { InstanceId = "instance", ExecutionId = "previous" };
        HistoryEvent[] history = CreateHistory(instance);
        Task<GrpcOrchestratorExecutionResult> episode = executor.ExecuteOrchestrator(instance, history, []);
        await workItems.Reader.ReadAsync(timeout.Token);
        TaskCompletionSource firstChunkWritten = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseReader = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<P.HistoryChunk> chunks = new();
        Mock<IServerStreamWriter<P.HistoryChunk>> historyWriter = new();
        historyWriter.Setup(w => w.WriteAsync(It.IsAny<P.HistoryChunk>())).Returns<P.HistoryChunk>(async chunk =>
        {
            chunks.Add(chunk.Clone());
            if (chunks.Count == 1)
            {
                firstChunkWritten.TrySetResult();
                await releaseReader.Task.WaitAsync(timeout.Token);
            }
        });
        Task reader = server.StreamInstanceHistory(
            new() { InstanceId = instance.InstanceId, ForWorkItemProcessing = true },
            historyWriter.Object, CreateContext(timeout.Token));

        try
        {
            await firstChunkWritten.Task.WaitAsync(timeout.Token);

            // Act
            await server.CompleteOrchestratorTask(new() { InstanceId = instance.InstanceId }, CreateContext());
            await episode.WaitAsync(timeout.Token);

            // Assert
            Assert.Equal(0, WorkerHistorySnapshotTestHelpers.GetSnapshots(server).Count);
            OrchestrationInstance next = new() { InstanceId = instance.InstanceId, ExecutionId = "current" };
            HistoryEvent[] nextHistory = CreateHistory(next, 'y');
            Task<GrpcOrchestratorExecutionResult> nextEpisode = executor.ExecuteOrchestrator(next, nextHistory, []);
            P.OrchestratorRequest nextRequest = (await workItems.Reader.ReadAsync(timeout.Token)).OrchestratorRequest;
            Assert.Equal(next.ExecutionId, nextRequest.ExecutionId);
            Assert.True(nextRequest.RequiresHistoryStreaming);
            Assert.Equal(nextHistory.Select(ProtobufUtils.ToHistoryEventProto),
                await ReadWorkerHistoryAsync(server, next.InstanceId));

            releaseReader.TrySetResult();
            await reader.WaitAsync(timeout.Token);
            Assert.Equal(2, chunks.Count);
            Assert.Equal(history.Select(ProtobufUtils.ToHistoryEventProto), chunks.SelectMany(chunk => chunk.Events));
            Assert.Equal(1, WorkerHistorySnapshotTestHelpers.GetSnapshots(server).Count);
            Assert.Equal(nextHistory.Select(ProtobufUtils.ToHistoryEventProto),
                WorkerHistorySnapshotTestHelpers.GetSnapshots(server)[next.InstanceId]);
            Assert.False(nextEpisode.IsCompleted);

            await server.CompleteOrchestratorTask(new() { InstanceId = next.InstanceId }, CreateContext());
            await nextEpisode.WaitAsync(timeout.Token);
            Assert.Equal(0, WorkerHistorySnapshotTestHelpers.GetSnapshots(server).Count);
        }
        finally
        {
            releaseReader.TrySetResult();
            await reader;
            timeout.Cancel();
            await connection;
        }
    }

    static HistoryEvent[] CreateHistory(OrchestrationInstance instance, char payload = 'x') =>
    [
        new ExecutionStartedEvent(-1, new string(payload, 600 * 1024))
        {
            Name = "PayloadLength",
            Version = string.Empty,
            OrchestrationInstance = instance,
        },
        new TaskScheduledEvent(0)
        {
            Name = "Length",
            Version = string.Empty,
            Input = new string(payload, 600 * 1024),
        },
    ];

    static Mock<IServerStreamWriter<P.WorkItem>> CreateWorkItemWriter(Channel<P.WorkItem> workItems)
    {
        Mock<IServerStreamWriter<P.WorkItem>> writer = new();
        writer.Setup(w => w.WriteAsync(It.IsAny<P.WorkItem>()))
            .Returns<P.WorkItem>(workItem => workItems.Writer.WriteAsync(workItem).AsTask());
        return writer;
    }

    static async Task<List<P.HistoryEvent>> ReadWorkerHistoryAsync(TaskHubGrpcServer server, string instanceId)
    {
        List<P.HistoryEvent> events = new();
        Mock<IServerStreamWriter<P.HistoryChunk>> writer = new();
        writer.Setup(w => w.WriteAsync(It.IsAny<P.HistoryChunk>())).Returns<P.HistoryChunk>(chunk =>
        {
            events.AddRange(chunk.Events);
            return Task.CompletedTask;
        });
        await server.StreamInstanceHistory(
            new() { InstanceId = instanceId, ForWorkItemProcessing = true }, writer.Object, CreateContext());
        return events;
    }

    static TaskHubGrpcServer CreateServer(CancellationToken stopping)
    {
        InMemoryOrchestrationService service = new();
        return new(
            Mock.Of<IHostApplicationLifetime>(lifetime => lifetime.ApplicationStopping == stopping),
            NullLoggerFactory.Instance, service, service, Options.Create(new TaskHubGrpcServerOptions()));
    }

    static ServerCallContext CreateContext(CancellationToken cancellation = default)
    {
        Mock<ServerCallContext> context = new();
        context.Protected().SetupGet<CancellationToken>("CancellationTokenCore").Returns(cancellation);
        return context.Object;
    }
}

static class WorkerHistorySnapshotTestHelpers
{
    internal static ConcurrentDictionary<string, List<P.HistoryEvent>> GetSnapshots(DurableTaskTestHost host)
    {
        FieldInfo field = typeof(DurableTaskTestHost).GetField("sidecarHost", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(DurableTaskTestHost), "sidecarHost");
        IHost sidecar = Assert.IsAssignableFrom<IHost>(field.GetValue(host));
        return GetSnapshots(sidecar.Services.GetRequiredService<TaskHubGrpcServer>());
    }

    internal static ConcurrentDictionary<string, List<P.HistoryEvent>> GetSnapshots(TaskHubGrpcServer server)
    {
        FieldInfo field = typeof(TaskHubGrpcServer).GetField("streamingPastEvents", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(TaskHubGrpcServer), "streamingPastEvents");
        return Assert.IsType<ConcurrentDictionary<string, List<P.HistoryEvent>>>(field.GetValue(server));
    }
}
