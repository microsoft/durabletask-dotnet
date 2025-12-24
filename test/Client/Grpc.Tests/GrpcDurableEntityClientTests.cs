// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Converters;
using Microsoft.Extensions.Logging;
using System.Linq;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Client.Grpc.Tests;

public class GrpcDurableEntityClientTests
{
    [Fact]
    public async Task CleanEntityStorageAsync_PurgesTransientEntities()
    {
        // Arrange
        var sidecar = new Mock<P.TaskHubSidecarService.TaskHubSidecarServiceClient>(MockBehavior.Strict, (CallInvoker)new Mock<CallInvoker>().Object);

        sidecar
            .Setup(c => c.CleanEntityStorageAsync(
                It.IsAny<P.CleanEntityStorageRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CompletedAsyncUnaryCall(new P.CleanEntityStorageResponse()));

        sidecar
            .Setup(c => c.QueryEntitiesAsync(
                It.IsAny<P.QueryEntitiesRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CompletedAsyncUnaryCall(new P.QueryEntitiesResponse
            {
                Entities =
                {
                    new P.EntityMetadata
                    {
                        InstanceId = "@entity@one",
                        SerializedState = string.Empty,
                        LockedBy = string.Empty,
                        BacklogQueueSize = 0,
                    },
                    new P.EntityMetadata
                    {
                        InstanceId = "@entity@two",
                        SerializedState = "state",
                    },
                },
            }));

        sidecar
            .Setup(c => c.PurgeInstancesAsync(
                It.Is<P.PurgeInstancesRequest>(r =>
                    r.InstanceBatch.InstanceIds.Count == 1
                    && r.InstanceBatch.InstanceIds[0] == "@entity@one"),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CompletedAsyncUnaryCall(new P.PurgeInstancesResponse
            {
                DeletedInstanceCount = 1,
            }));

        GrpcDurableEntityClient client = this.CreateClient(sidecar.Object);

        // Act
        CleanEntityStorageResult result = await client.CleanEntityStorageAsync(
            new CleanEntityStorageRequest
            {
                RemoveEmptyEntities = true,
                ReleaseOrphanedLocks = true,
            });

        // Assert
        result.EmptyEntitiesRemoved.Should().Be(1);
        result.OrphanedLocksReleased.Should().Be(0);

        sidecar.Verify(
            c => c.QueryEntitiesAsync(
                It.Is<P.QueryEntitiesRequest>(r => r.Query.IncludeTransient && r.Query.IncludeState),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        sidecar.Verify(
            c => c.PurgeInstancesAsync(
                It.IsAny<P.PurgeInstancesRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAllEntitiesAsync_IncludeState_ReportsStateIncludedWhenMissing()
    {
        // Arrange
        var sidecar = new Mock<P.TaskHubSidecarService.TaskHubSidecarServiceClient>(MockBehavior.Strict, (CallInvoker)new Mock<CallInvoker>().Object);

        sidecar
            .Setup(c => c.QueryEntitiesAsync(
                It.IsAny<P.QueryEntitiesRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CompletedAsyncUnaryCall(new P.QueryEntitiesResponse
            {
                Entities =
                {
                    new P.EntityMetadata
                    {
                        InstanceId = "@entity@missing",
                        SerializedState = string.Empty,
                        LockedBy = string.Empty,
                        BacklogQueueSize = 0,
                    },
                },
            }));

        GrpcDurableEntityClient client = this.CreateClient(sidecar.Object);

        // Act
        List<EntityMetadata> results = await client
            .GetAllEntitiesAsync(new EntityQuery { IncludeState = true, IncludeTransient = true })
            .ToListAsync();

        // Assert
        results.Should().ContainSingle();
        results[0].IncludesState.Should().BeTrue();
        results[0].State.Should().BeNull();
    }

    static AsyncUnaryCall<T> CompletedAsyncUnaryCall<T>(T response)
    {
        Task<T> respTask = Task.FromResult(response);
        return new AsyncUnaryCall<T>(
            respTask,
            Task.FromResult(new Metadata()),
            () => new Status(StatusCode.OK, string.Empty),
            () => new Metadata(),
            () => { });
    }

    GrpcDurableEntityClient CreateClient(P.TaskHubSidecarService.TaskHubSidecarServiceClient sidecar)
    {
        var logger = Mock.Of<ILogger>();
        return new GrpcDurableEntityClient("test", JsonDataConverter.Default, sidecar, logger);
    }
}
