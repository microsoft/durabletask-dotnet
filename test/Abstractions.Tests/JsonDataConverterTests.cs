// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Microsoft.DurableTask.Converters;

namespace Microsoft.DurableTask.Tests;

public class JsonDataConverterTests
{
    readonly JsonDataConverter converter = JsonDataConverter.Default;

    [Fact]
    public void Serialize_Null_ReturnsNull()
    {
        // Act
        string? result = this.converter.Serialize(null);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Deserialize_Null_ReturnsNull()
    {
        // Act
        object? result = this.converter.Deserialize(null, typeof(object));

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void RoundTrip_SimpleTypes_PreservesTypes()
    {
        // Arrange
        Dictionary<string, object> input = new()
        {
            { "string", "value" },
            { "int", 42 },
            { "long", 9223372036854775807L },
            { "double", 3.14 },
            { "bool", true },
            { "null", null! },
        };

        // Act
        string serialized = this.converter.Serialize(input)!;
        Dictionary<string, object>? result = this.converter.Deserialize<Dictionary<string, object>>(serialized);

        // Assert
        result.Should().NotBeNull();
        result!["string"].Should().BeOfType<string>().And.Be("value");
        result["int"].Should().BeOfType<int>().And.Be(42);
        
        // Note: Large integers that don't fit in int32 will be deserialized as long
        result["long"].Should().Match<object>(o => o is long || o is double);
        
        result["double"].Should().BeOfType<double>().And.Be(3.14);
        result["bool"].Should().BeOfType<bool>().And.Be(true);
        result["null"].Should().BeNull();
    }

    [Fact]
    public void RoundTrip_NestedDictionary_PreservesStructure()
    {
        // Arrange
        Dictionary<string, object> input = new()
        {
            {
                "nested", new Dictionary<string, object>
                {
                    { "key1", "value1" },
                    { "key2", 123 },
                }
            },
        };

        // Act
        string serialized = this.converter.Serialize(input)!;
        Dictionary<string, object>? result = this.converter.Deserialize<Dictionary<string, object>>(serialized);

        // Assert
        result.Should().NotBeNull();
        result!["nested"].Should().BeOfType<Dictionary<string, object>>();
        Dictionary<string, object> nested = (Dictionary<string, object>)result["nested"];
        nested["key1"].Should().BeOfType<string>().And.Be("value1");
        nested["key2"].Should().BeOfType<int>().And.Be(123);
    }

    [Fact]
    public void RoundTrip_Array_PreservesElements()
    {
        // Arrange
        Dictionary<string, object> input = new()
        {
            { "array", new object[] { "string", 42, true } },
        };

        // Act
        string serialized = this.converter.Serialize(input)!;
        Dictionary<string, object>? result = this.converter.Deserialize<Dictionary<string, object>>(serialized);

        // Assert
        result.Should().NotBeNull();
        result!["array"].Should().BeOfType<object[]>();
        object[] array = (object[])result["array"];
        array.Should().HaveCount(3);
        array[0].Should().BeOfType<string>().And.Be("string");
        array[1].Should().BeOfType<int>().And.Be(42);
        array[2].Should().BeOfType<bool>().And.Be(true);
    }

    [Fact]
    public void RoundTrip_ComplexObject_PreservesStructure()
    {
        // Arrange - simulate the issue described in the GitHub issue
        Dictionary<string, object> input = new()
        {
            { "ComponentContext", new { Name = "TestComponent", Id = 123 } },
            { "PlanResult", new { Status = "Success", Count = 5 } },
        };

        // Act
        string serialized = this.converter.Serialize(input)!;
        Dictionary<string, object>? result = this.converter.Deserialize<Dictionary<string, object>>(serialized);

        // Assert
        result.Should().NotBeNull();
        
        // The anonymous objects should be deserialized as dictionaries, not JsonElements
        result!["ComponentContext"].Should().BeOfType<Dictionary<string, object>>();
        Dictionary<string, object> componentContext = (Dictionary<string, object>)result["ComponentContext"];
        componentContext["Name"].Should().BeOfType<string>().And.Be("TestComponent");
        componentContext["Id"].Should().BeOfType<int>().And.Be(123);

        result["PlanResult"].Should().BeOfType<Dictionary<string, object>>();
        Dictionary<string, object> planResult = (Dictionary<string, object>)result["PlanResult"];
        planResult["Status"].Should().BeOfType<string>().And.Be("Success");
        planResult["Count"].Should().BeOfType<int>().And.Be(5);
    }

    [Fact]
    public void Deserialize_JsonWithoutConverter_ProducesJsonElements()
    {
        // Arrange - use standard System.Text.Json without our custom converter
        JsonSerializerOptions standardOptions = new() { IncludeFields = true };
        Dictionary<string, object> input = new()
        {
            { "key", "value" },
        };
        string json = JsonSerializer.Serialize(input, standardOptions);

        // Act
        Dictionary<string, object>? result = JsonSerializer.Deserialize<Dictionary<string, object>>(json, standardOptions);

        // Assert - without our converter, values are JsonElements
        result.Should().NotBeNull();
        result!["key"].Should().BeOfType<JsonElement>();
    }

    [Fact]
    public void Deserialize_JsonWithConverter_ProducesConcreteTypes()
    {
        // Arrange
        Dictionary<string, object> input = new()
        {
            { "key", "value" },
        };
        string json = this.converter.Serialize(input)!;

        // Act
        Dictionary<string, object>? result = this.converter.Deserialize<Dictionary<string, object>>(json);

        // Assert - with our converter, values are concrete types
        result.Should().NotBeNull();
        result!["key"].Should().BeOfType<string>().And.Be("value");
    }

    [Fact]
    public void RoundTrip_RecordType_PreservesProperties()
    {
        // Arrange
        TestRecord input = new("TestValue", 42, new Dictionary<string, object>
        {
            { "nested", "data" },
        });

        // Act
        string serialized = this.converter.Serialize(input)!;
        TestRecord? result = this.converter.Deserialize<TestRecord>(serialized);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("TestValue");
        result.Value.Should().Be(42);
        result.Properties.Should().ContainKey("nested");
        result.Properties["nested"].Should().BeOfType<string>().And.Be("data");
    }

    record TestRecord(string Name, int Value, Dictionary<string, object> Properties);
}
