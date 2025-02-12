// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq.Expressions;
using DotNext;
using DurableTask.Core;
using DurableTask.Core.Entities;
using DurableTask.Core.History;
using FluentAssertions.Execution;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Entities;

namespace Microsoft.DurableTask.Client.OrchestrationServiceClientShim.Tests;

public class ShimDurableEntityClientTests
{
    static readonly DataConverter Converter = new JsonDataConverter();
    readonly Mock<EntityBackendQueries> query = new(MockBehavior.Strict);
    readonly Mock<IOrchestrationServiceClient> client = new(MockBehavior.Strict);

    [Fact]
    public void Ctor_NullOptions_Throws()
    {
        Func<object> act = static () => new ShimDurableEntityClient("test", null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public async Task CleanEntityStorageAsync_NullRequest_ReturnsExpectedResult()
    {
        EntityBackendQueries.CleanEntityStorageResult result = new()
        {
            EmptyEntitiesRemoved = Random.Shared.Next(0, 100),
            OrphanedLocksReleased = Random.Shared.Next(0, 100),
            ContinuationToken = Guid.NewGuid().ToString(),
        };

        Expression<Func<EntityBackendQueries.CleanEntityStorageRequest, bool>> verifyRequest = r =>
            r.RemoveEmptyEntities == true
            && r.ReleaseOrphanedLocks == true
            && r.ContinuationToken == null;
        this.query.Setup(x => x.CleanEntityStorageAsync(It.Is(verifyRequest), default)).ReturnsAsync(result);

        ShimDurableEntityClient client = this.CreateEntityClient();
        CleanEntityStorageResult actual = await client.CleanEntityStorageAsync();

        actual.Should().BeEquivalentTo(new CleanEntityStorageResult
        {
            EmptyEntitiesRemoved = result.EmptyEntitiesRemoved,
            OrphanedLocksReleased = result.OrphanedLocksReleased,
            ContinuationToken = result.ContinuationToken,
        });
    }

    [Theory, CombinatorialData]
    public async Task CleanEntityStorageAsync_SuppliedRequest_ReturnsExpectedResult(
        bool removeEmptyEntities, bool releaseOrphanedLocks, bool continuationToken)
    {
        CleanEntityStorageRequest request = new()
        {
            RemoveEmptyEntities = removeEmptyEntities,
            ReleaseOrphanedLocks = releaseOrphanedLocks,
            ContinuationToken = continuationToken ? Guid.NewGuid().ToString() : null,
        };

        EntityBackendQueries.CleanEntityStorageResult result = new()
        {
            EmptyEntitiesRemoved = Random.Shared.Next(0, 100),
            OrphanedLocksReleased = Random.Shared.Next(0, 100),
            ContinuationToken = Guid.NewGuid().ToString(),
        };

        Expression<Func<EntityBackendQueries.CleanEntityStorageRequest, bool>> verifyRequest = r =>
            r.RemoveEmptyEntities == request.RemoveEmptyEntities
            && r.ReleaseOrphanedLocks == request.ReleaseOrphanedLocks
            && r.ContinuationToken == request.ContinuationToken;
        this.query.Setup(x => x.CleanEntityStorageAsync(It.Is(verifyRequest), default)).ReturnsAsync(result);

        ShimDurableEntityClient client = this.CreateEntityClient();
        CleanEntityStorageResult actual = await client.CleanEntityStorageAsync(request);

        actual.Should().BeEquivalentTo(new CleanEntityStorageResult
        {
            EmptyEntitiesRemoved = result.EmptyEntitiesRemoved,
            OrphanedLocksReleased = result.OrphanedLocksReleased,
            ContinuationToken = result.ContinuationToken,
        });
    }

    [Fact]
    public async Task GetAllEntitiesAsync_NoFilter_ReturnsExpectedResult()
    {
        List<EntityBackendQueries.EntityMetadata> entities = [..
            Enumerable.Range(0, 25).Select(i => CreateCoreMetadata(i))];

        string? continuationToken = null;
        foreach (IEnumerable<EntityBackendQueries.EntityMetadata> batch in entities.Chunk(10))
        {
            EntityBackendQueries.EntityQuery filter = new()
            {
                PageSize = null,
                IncludeState = true,
                IncludeTransient = false,
                InstanceIdStartsWith = string.Empty,
                LastModifiedFrom = null,
                LastModifiedTo = null,
                ContinuationToken = continuationToken,
            };

            List<EntityBackendQueries.EntityMetadata> values = [.. batch];
            continuationToken = values.Count == 10 ? Guid.NewGuid().ToString() : null;
            EntityBackendQueries.EntityQueryResult result = new()
            {
                Results = values,
                ContinuationToken = continuationToken,
            };

            this.query.Setup(x => x.QueryEntitiesAsync(filter, default)).ReturnsAsync(result);
        }

        ShimDurableEntityClient client = this.CreateEntityClient();
        List<EntityMetadata> actualEntities = await client.GetAllEntitiesAsync().ToListAsync();

        using AssertionScope scope = new();
        actualEntities.Should().HaveCount(entities.Count);

        foreach ((EntityMetadata actual, EntityBackendQueries.EntityMetadata expected) in actualEntities.Zip(entities))
        {
            VerifyEntity(actual, expected);
        }
    }

    [Fact]
    public async Task GetAllEntitiesAsync_WithFilter_ReturnsExpectedResult()
    {
        List<EntityBackendQueries.EntityMetadata> entities = [..
            Enumerable.Range(0, 25).Select(i => CreateCoreMetadata(i))];

        string? continuationToken = Guid.NewGuid().ToString();
        EntityQuery query = new() { IncludeState = false, PageSize = 10, ContinuationToken = continuationToken };
        foreach (IEnumerable<EntityBackendQueries.EntityMetadata> batch in entities.Chunk(10))
        {
            EntityBackendQueries.EntityQuery filter = new()
            {
                PageSize = 10,
                IncludeState = false,
                IncludeTransient = false,
                InstanceIdStartsWith = string.Empty,
                LastModifiedFrom = null,
                LastModifiedTo = null,
                ContinuationToken = continuationToken,
            };

            List<EntityBackendQueries.EntityMetadata> values = [.. batch];
            continuationToken = values.Count == 10 ? Guid.NewGuid().ToString() : null;
            EntityBackendQueries.EntityQueryResult result = new()
            {
                Results = values,
                ContinuationToken = continuationToken,
            };

            this.query.Setup(x => x.QueryEntitiesAsync(filter, default)).ReturnsAsync(result);
        }

        ShimDurableEntityClient client = this.CreateEntityClient();
        List<EntityMetadata> actualEntities = await client.GetAllEntitiesAsync(query).ToListAsync();

        using AssertionScope scope = new();
        actualEntities.Should().HaveCount(entities.Count);

        foreach ((EntityMetadata actual, EntityBackendQueries.EntityMetadata expected) in actualEntities.Zip(entities))
        {
            VerifyEntity(actual, expected);
        }
    }

    [Fact]
    public async Task GetAllEntitiesAsyncOfT_NoFilter_ReturnsExpectedResult()
    {
        List<EntityBackendQueries.EntityMetadata> entities = [..
            Enumerable.Range(0, 25).Select(i => CreateCoreMetadata(i, $"state-{i}"))];

        string? continuationToken = null;
        foreach (IEnumerable<EntityBackendQueries.EntityMetadata> batch in entities.Chunk(10))
        {
            EntityBackendQueries.EntityQuery filter = new()
            {
                PageSize = null,
                IncludeState = true,
                IncludeTransient = false,
                InstanceIdStartsWith = string.Empty,
                LastModifiedFrom = null,
                LastModifiedTo = null,
                ContinuationToken = continuationToken,
            };

            List<EntityBackendQueries.EntityMetadata> values = [.. batch];
            continuationToken = values.Count == 10 ? Guid.NewGuid().ToString() : null;
            EntityBackendQueries.EntityQueryResult result = new()
            {
                Results = values,
                ContinuationToken = continuationToken,
            };

            this.query.Setup(x => x.QueryEntitiesAsync(filter, default)).ReturnsAsync(result);
        }

        ShimDurableEntityClient client = this.CreateEntityClient();
        List<EntityMetadata<string>> actualEntities = await client.GetAllEntitiesAsync<string>().ToListAsync();

        using AssertionScope scope = new();
        actualEntities.Should().HaveCount(entities.Count);

        foreach ((EntityMetadata<string> actual, EntityBackendQueries.EntityMetadata expected) in actualEntities.Zip(entities))
        {
            VerifyEntity(actual, expected, $"state-{actual.Id.Key}");
        }
    }

    [Fact]
    public async Task GetAllEntitiesAsyncOfT_WithFilter_ReturnsExpectedResult()
    {
        List<EntityBackendQueries.EntityMetadata> entities = [..
            Enumerable.Range(0, 25).Select(i => CreateCoreMetadata(i, $"state-{i}"))];

        string? continuationToken = Guid.NewGuid().ToString();
        EntityQuery query = new() { IncludeState = true, PageSize = 10, ContinuationToken = continuationToken };
        foreach (IEnumerable<EntityBackendQueries.EntityMetadata> batch in entities.Chunk(10))
        {
            EntityBackendQueries.EntityQuery filter = new()
            {
                PageSize = 10,
                IncludeState = true,
                IncludeTransient = false,
                InstanceIdStartsWith = string.Empty,
                LastModifiedFrom = null,
                LastModifiedTo = null,
                ContinuationToken = continuationToken,
            };

            List<EntityBackendQueries.EntityMetadata> values = [.. batch];
            continuationToken = values.Count == 10 ? Guid.NewGuid().ToString() : null;
            EntityBackendQueries.EntityQueryResult result = new()
            {
                Results = values,
                ContinuationToken = continuationToken,
            };

            this.query.Setup(x => x.QueryEntitiesAsync(filter, default)).ReturnsAsync(result);
        }

        ShimDurableEntityClient client = this.CreateEntityClient();
        List<EntityMetadata<string>> actualEntities = await client.GetAllEntitiesAsync<string>(query).ToListAsync();

        using AssertionScope scope = new();
        actualEntities.Should().HaveCount(entities.Count);

        foreach ((EntityMetadata<string> actual, EntityBackendQueries.EntityMetadata expected) in actualEntities.Zip(entities))
        {
            VerifyEntity(actual, expected, $"state-{actual.Id.Key}");
        }
    }

    [Theory, CombinatorialData]
    public async Task GetEntityAsync_Success(bool includeState)
    {
        EntityBackendQueries.EntityMetadata expected = CreateCoreMetadata(0, includeState ? "state" : null);
        this.query.Setup(x => x.GetEntityAsync(expected.EntityId, includeState, false, default)).ReturnsAsync(expected);

        ShimDurableEntityClient client = this.CreateEntityClient();
        EntityMetadata? entity = await client.GetEntityAsync(expected.EntityId.ConvertFromCore(), includeState);

        entity.Should().NotBeNull();
        VerifyEntity(entity!, expected);
        entity!.IncludesState.Should().Be(includeState);
        
        if (includeState)
        {
            entity!.State.Value.Should().Be("\"state\"");
        }
    }

    [Theory, CombinatorialData]
    public async Task GetEntityAsyncOfT_Success(bool includeState)
    {
        EntityBackendQueries.EntityMetadata expected = CreateCoreMetadata(0, includeState ? "state" : null);
        this.query.Setup(x => x.GetEntityAsync(expected.EntityId, includeState, false, default)).ReturnsAsync(expected);

        ShimDurableEntityClient client = this.CreateEntityClient();
        EntityMetadata<string>? entity = await client.GetEntityAsync<string>(
            expected.EntityId.ConvertFromCore(), includeState);

        entity.Should().NotBeNull();
        VerifyEntity(entity!, expected);
        entity!.IncludesState.Should().Be(includeState);
        
        if (includeState)
        {
            entity!.State.Should().Be("state");
        }
    }

    [Fact]
    public async Task SignalEntityAsync_Success()
    {
        EntityInstanceId id = new("test", "0");
        string operationName = "op";
        object input = new { Value = 42 };

        Func<TaskMessage, bool> isExpectedMessage = m =>
            m.OrchestrationInstance.InstanceId == id.ToString()
            && m.Event is EventRaisedEvent e
            && e.Name == operationName
            && !string.IsNullOrEmpty(e.Input);
        this.client.Setup(x => x.SendTaskOrchestrationMessageAsync(It.Is<TaskMessage>(m => isExpectedMessage(m))))
            .Returns(Task.CompletedTask);

        ShimDurableEntityClient client = this.CreateEntityClient();
        await client.SignalEntityAsync(id, operationName, input);

        this.client.Verify(x => x.SendTaskOrchestrationMessageAsync(It.IsNotNull<TaskMessage>()), Times.Once());
    }

    static EntityBackendQueries.EntityMetadata CreateCoreMetadata(int i, object? state = null)
    {
        return new()
        {
            EntityId = new("test", i.ToString()),
            BacklogQueueSize = Random.Shared.Next(0, 10),
            LastModifiedTime = Random.Shared.NextDateTime(TimeSpan.FromDays(-10)),
            LockedBy = Random.Shared.NextBoolean() ? Guid.NewGuid().ToString() : null,
            SerializedState = state is null ? null : Converter.Serialize(state),
        };
    }

    static void VerifyEntity<TState>(
        EntityMetadata<TState> actual, EntityBackendQueries.EntityMetadata expected)
    {
        actual.Id.Should().Be(expected.EntityId.ConvertFromCore());
        actual.BacklogQueueSize.Should().Be(expected.BacklogQueueSize);
        actual.LastModifiedTime.Should().Be(new DateTimeOffset(expected.LastModifiedTime));
        actual.LockedBy.Should().Be(expected.LockedBy);
    }

    static void VerifyEntity<TState>(
        EntityMetadata<TState> actual, EntityBackendQueries.EntityMetadata expected, TState? state)
    {
        VerifyEntity(actual, expected);
        actual.State.Should().Be(state);
    }

    ShimDurableEntityClient CreateEntityClient()
    {
        ShimDurableTaskClientOptions options = new()
        {
            DataConverter = Converter,
            Client = this.client.Object,
            Entities = { Queries = this.query.Object, },
        };

        return new ShimDurableEntityClient("test", options);
    }
}
