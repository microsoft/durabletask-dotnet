// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Dapr.DurableTask.Entities;

namespace Dapr.DurableTask.Client.Entities.Tests;

public class EntityMetadataTests
{
    readonly EntityInstanceId id = new("test", Random.Shared.Next(0, 100).ToString());

    [Fact]
    public void GetState_NotIncluded_Throws()
    {
        EntityMetadata<int> metadata = new(this.id);
        Func<int> act = () => metadata.State;
        act.Should().ThrowExactly<InvalidOperationException>();
    }

    [Fact]
    public void GetState_Included_DoesNotThrow()
    {
        int state = Random.Shared.Next(0, 100);
        EntityMetadata<int> metadata = new(this.id, state);
        metadata.State.Should().Be(state);
    }

    [Fact]
    public void Serialize_StateNotIncluded()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string lockedBy = Guid.NewGuid().ToString("N");
        EntityMetadata<int> metadata = new(this.id)
        {
            LastModifiedTime = now,
            BacklogQueueSize = 10,
            LockedBy = lockedBy,
        };

        string nowStr = JsonSerializer.Serialize(now); // STJ drops trailing zeroes from dates. see: https://github.com/microsoft/durabletask-dotnet/pull/239
        string json = JsonSerializer.Serialize(metadata);
        json.Should().Be($"{{\"Id\":\"{this.id}\",\"LastModifiedTime\":{nowStr},\"BacklogQueueSize\":10,\"LockedBy\""
            + $":\"{lockedBy}\"}}");
    }

    [Fact]
    public void Serialize_StateIncluded_Int()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string lockedBy = Guid.NewGuid().ToString("N");
        int state = Random.Shared.Next(0, 100);
        EntityMetadata<int> metadata = new(this.id, state)
        {
            LastModifiedTime = now,
            BacklogQueueSize = 10,
            LockedBy = lockedBy,
        };

        string nowStr = JsonSerializer.Serialize(now); // STJ drops trailing zeroes from dates. see: https://github.com/microsoft/durabletask-dotnet/pull/239
        string json = JsonSerializer.Serialize(metadata);
        json.Should().Be($"{{\"Id\":\"{this.id}\",\"LastModifiedTime\":{nowStr},\"BacklogQueueSize\":10,\"LockedBy\""
            + $":\"{lockedBy}\",\"State\":{state}}}");
    }

    [Fact]
    public void Serialize_StateIncluded_Object()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string lockedBy = Guid.NewGuid().ToString("N");
        State state = State.GetRandom();
        EntityMetadata<State> metadata = new(this.id, state)
        {
            LastModifiedTime = now,
            BacklogQueueSize = 10,
            LockedBy = lockedBy,
        };

        string nowStr = JsonSerializer.Serialize(now); // STJ drops trailing zeroes from dates. see: https://github.com/microsoft/durabletask-dotnet/pull/239
        string json = JsonSerializer.Serialize(metadata);
        json.Should().Be($"{{\"Id\":\"{this.id}\",\"LastModifiedTime\":{nowStr},\"BacklogQueueSize\":10,\"LockedBy\""
            + $":\"{lockedBy}\",\"State\":{{\"Number\":{state.Number}}}}}");
    }
    
    [Fact]
    public void Serialize_StateIncluded_Object2()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        State state = State.GetRandom();
        EntityMetadata<State> metadata = new(this.id, state)
        {
            LastModifiedTime = now,
        };

        string nowStr = JsonSerializer.Serialize(now); // STJ drops trailing zeroes from dates. see: https://github.com/microsoft/durabletask-dotnet/pull/239
        string json = JsonSerializer.Serialize(metadata);
        json.Should().Be($"{{\"Id\":\"{this.id}\",\"LastModifiedTime\":{nowStr},\"State\":"
            + $"{{\"Number\":{state.Number}}}}}");
    }

    [Fact]
    public void Serialize_StateNotIncluded_NonGeneric()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        State state = State.GetRandom();
        EntityMetadata metadata = new(this.id)
        {
            LastModifiedTime = now,
        };

        string nowStr = JsonSerializer.Serialize(now); // STJ drops trailing zeroes from dates. see: https://github.com/microsoft/durabletask-dotnet/pull/239
        string json = JsonSerializer.Serialize(metadata);
        json.Should().Be($"{{\"Id\":\"{this.id}\",\"LastModifiedTime\":{nowStr}}}");
    }


    [Fact]
    public void Serialize_StateIncluded_NonGeneric()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        State state = State.GetRandom();
        EntityMetadata metadata = new(this.id, SerializedData.Create(state))
        {
            LastModifiedTime = now,
        };

        string nowStr = JsonSerializer.Serialize(now); // STJ drops trailing zeroes from dates. see: https://github.com/microsoft/durabletask-dotnet/pull/239
        string json = JsonSerializer.Serialize(metadata);
        json.Should().Be($@"{{""Id"":""{this.id}"",""LastModifiedTime"":{nowStr},""State"":""{{\u0022Number\u0022:{state.Number}}}""}}");
    }

    record class State(int Number)
    {
        public static State GetRandom() => new(Random.Shared.Next(0, 100));
    }
}