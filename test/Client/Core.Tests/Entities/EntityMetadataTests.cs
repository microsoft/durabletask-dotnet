// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.DurableTask.Entities;

namespace Microsoft.DurableTask.Client.Entities.Tests;

public class EntityMetadataTests
{
    readonly EntityInstanceId id = new("test", Random.Shared.Next(0, 100).ToString());

    // create customize convert class
    public class DateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return DateTime.Parse(reader.GetString());
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString("dd/MM/yyyy"));
        }
    }

    readonly JsonSerializerOptions settings = new JsonSerializerOptions() {
        Converters = { new DateTimeConverter() }
    };

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

        string json = JsonSerializer.Serialize(metadata, settings);
        json.Should().Be($"{{\"Id\":\"{this.id}\",\"LastModifiedTime\":\"{now:O}\",\"BacklogQueueSize\":10,\"LockedBy\""
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

        string json = JsonSerializer.Serialize(metadata,settings);
        json.Should().Be($"{{\"Id\":\"{this.id}\",\"LastModifiedTime\":\"{now:O}\",\"BacklogQueueSize\":10,\"LockedBy\""
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

        string json = JsonSerializer.Serialize(metadata, settings);
        json.Should().Be($"{{\"Id\":\"{this.id}\",\"LastModifiedTime\":\"{now:O}\",\"BacklogQueueSize\":10,\"LockedBy\""
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

        string json = JsonSerializer.Serialize(metadata, settings);
        json.Should().Be($"{{\"Id\":\"{this.id}\",\"LastModifiedTime\":\"{now:O}\",\"State\":"
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

        string json = JsonSerializer.Serialize(metadata, settings);
        json.Should().Be($"{{\"Id\":\"{this.id}\",\"LastModifiedTime\":\"{now:O}\"}}");
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

        string json = JsonSerializer.Serialize(metadata, settings);
        json.Should().Be($@"{{""Id"":""{this.id}"",""LastModifiedTime"":""{now:O}"",""State"":""{{\u0022Number\u0022:{state.Number}}}""}}");
    }

    record class State(int Number)
    {
        public static State GetRandom() => new(Random.Shared.Next(0, 100));
    }
}