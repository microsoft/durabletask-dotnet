// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.DurableTask.Converters;

namespace Microsoft.DurableTask.Tests.Converters;

public class JsonDataConverterTests
{
    // Test types matching ConsoleAppMinimal sample
    public record ComponentContext(string Name, string Type, List<string> Dependencies);
    public record PlanResult(bool Success, int Status, string Reason);
    public record TestActivityInput(Dictionary<string, object> Properties);

    [Fact]
    public void SerializeDeserialize_DictionaryWithComplexTypes_PreservesTypes()
    {
        // Arrange
        var converter = JsonDataConverter.Default;
        var componentContext = new ComponentContext(
            Name: "loganalytics",
            Type: "terraform",
            Dependencies: ["resourcegroup"]);

        var planResult = new PlanResult(
            Success: true,
            Status: 2,
            Reason: "replace_because_tainted");

        var input = new TestActivityInput(new Dictionary<string, object>
        {
            { "ComponentContext", componentContext },
            { "PlanResult", planResult }
        });

        // Act
        string serialized = converter.Serialize(input);
        TestActivityInput? deserialized = converter.Deserialize<TestActivityInput>(serialized);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Properties.Should().NotBeNull().And.NotBeEmpty();
        deserialized.Properties.Should().HaveCount(2);

        // Verify ComponentContext is preserved (not JsonElement)
        deserialized.Properties["ComponentContext"].Should().BeOfType<ComponentContext>();
        var deserializedComponent = (ComponentContext)deserialized.Properties["ComponentContext"];
        deserializedComponent.Name.Should().Be("loganalytics");
        deserializedComponent.Type.Should().Be("terraform");
        deserializedComponent.Dependencies.Should().Equal(["resourcegroup"]);

        // Verify PlanResult is preserved (not JsonElement)
        deserialized.Properties["PlanResult"].Should().BeOfType<PlanResult>();
        var deserializedPlan = (PlanResult)deserialized.Properties["PlanResult"];
        deserializedPlan.Success.Should().BeTrue();
        deserializedPlan.Status.Should().Be(2);
        deserializedPlan.Reason.Should().Be("replace_because_tainted");
    }

    [Fact]
    public void SerializeDeserialize_DirectComplexType_PreservesType()
    {
        // Arrange
        var converter = JsonDataConverter.Default;
        var componentContext = new ComponentContext(
            Name: "test",
            Type: "type",
            Dependencies: ["dep1", "dep2"]);

        // Act
        string serialized = converter.Serialize(componentContext);
        ComponentContext? deserialized = converter.Deserialize<ComponentContext>(serialized);

        // Assert
        deserialized.Should().NotBeNull();
        ComponentContext result = deserialized!;
        result.Name.Should().Be("test");
        result.Type.Should().Be("type");
        result.Dependencies.Should().Equal(["dep1", "dep2"]);
    }

    [Fact]
    public void SerializeDeserialize_DictionaryWithPrimitives_PreservesTypes()
    {
        // Arrange
        var converter = JsonDataConverter.Default;
        var input = new Dictionary<string, object>
        {
            { "StringValue", "test" },
            { "IntValue", 42 },
            { "BoolValue", true },
            { "DoubleValue", 3.14 },
            { "NullValue", null! }
        };

        // Act
        string serialized = converter.Serialize(input);
        Dictionary<string, object>? deserialized = converter.Deserialize<Dictionary<string, object>>(serialized);

        // Assert
        deserialized.Should().NotBeNull();
        Dictionary<string, object> result = deserialized!;
        result.Should().HaveCount(5);
        // Note: Primitives in Dictionary<string, object> are deserialized as JsonElement
        // because they don't have type metadata (primitives are not wrapped).
        // This is expected behavior - the converter only wraps complex types.
        result["StringValue"].Should().BeOfType<JsonElement>().Subject.GetString().Should().Be("test");
        result["IntValue"].Should().BeOfType<JsonElement>().Subject.GetInt32().Should().Be(42);
        result["BoolValue"].Should().BeOfType<JsonElement>().Subject.GetBoolean().Should().Be(true);
        result["DoubleValue"].Should().BeOfType<JsonElement>().Subject.GetDouble().Should().BeApproximately(3.14, 0.01);
        result["NullValue"].Should().BeNull();
    }

    [Fact]
    public void SerializeDeserialize_DictionaryWithNestedObjects_PreservesTypes()
    {
        // Arrange
        var converter = JsonDataConverter.Default;
        var inner = new ComponentContext("inner", "type", ["dep"]);
        var input = new Dictionary<string, object>
        {
            { "Outer", new Dictionary<string, object> { { "Inner", inner } } }
        };

        // Act
        string serialized = converter.Serialize(input);
        Dictionary<string, object>? deserialized = converter.Deserialize<Dictionary<string, object>>(serialized);

        // Assert
        deserialized.Should().NotBeNull();
        Dictionary<string, object> result = deserialized!;
        var outer = result["Outer"].Should().BeOfType<Dictionary<string, object>>().Subject;
        var innerDeserialized = outer["Inner"].Should().BeOfType<ComponentContext>().Subject;
        innerDeserialized.Name.Should().Be("inner");
    }

    [Fact]
    public void SerializeDeserialize_ArrayOfComplexTypes_PreservesTypes()
    {
        // Arrange
        var converter = JsonDataConverter.Default;
        var items = new ComponentContext[]
        {
            new("item1", "type1", ["dep1"]),
            new("item2", "type2", ["dep2"])
        };

        // Act
        string serialized = converter.Serialize(items);
        ComponentContext[]? deserialized = converter.Deserialize<ComponentContext[]>(serialized);

        // Assert
        deserialized.Should().NotBeNull();
        ComponentContext[] result = deserialized!;
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("item1");
        result[1].Name.Should().Be("item2");
    }

    [Fact]
    public void SerializeDeserialize_DictionaryWithArray_PreservesTypes()
    {
        // Arrange
        var converter = JsonDataConverter.Default;
        var input = new Dictionary<string, object>
        {
            { "Items", new ComponentContext[]
            {
                new("item1", "type1", ["dep1"]),
                new("item2", "type2", ["dep2"])
            } }
        };

        // Act
        string serialized = converter.Serialize(input);
        Dictionary<string, object>? deserialized = converter.Deserialize<Dictionary<string, object>>(serialized);

        // Assert
        deserialized.Should().NotBeNull();
        Dictionary<string, object> result = deserialized!;
        var items = result["Items"].Should().BeOfType<ComponentContext[]>().Subject;
        items.Should().HaveCount(2);
        items[0].Name.Should().Be("item1");
        items[1].Name.Should().Be("item2");
    }

    [Fact]
    public void SerializeDeserialize_NullValue_HandlesCorrectly()
    {
        // Arrange
        var converter = JsonDataConverter.Default;

        // Act
        string? serialized = converter.Serialize(null);
        object? deserialized = converter.Deserialize<object>(serialized);

        // Assert
        serialized.Should().BeNull();
        deserialized.Should().BeNull();
    }

    [Fact]
    public void SerializeDeserialize_JsonElement_UnwrapsTypeMetadata()
    {
        // Arrange
        var converter = JsonDataConverter.Default;
        var input = new Dictionary<string, object>
        {
            { "Key", "Value" }
        };

        // Act
        string serialized = converter.Serialize(input);
        JsonElement deserialized = converter.Deserialize<JsonElement>(serialized);

        // Assert
        deserialized.ValueKind.Should().Be(JsonValueKind.Object);
        deserialized.TryGetProperty("Key", out JsonElement keyElement).Should().BeTrue();
        keyElement.GetString().Should().Be("Value");
    }

    [Fact]
    public void SerializeDeserialize_ObjectArray_DoesNotWrap()
    {
        // Arrange
        var converter = JsonDataConverter.Default;
        object[] input = ["item1", "item2", "item3"];

        // Act
        string serialized = converter.Serialize(input);
        object[]? deserialized = converter.Deserialize<object[]>(serialized);

        // Assert
        deserialized.Should().NotBeNull();
        object[] result = deserialized!;
        result.Should().HaveCount(3);
        // Note: Elements in object[] may be deserialized as JsonElement if they don't have type metadata
        // This is expected behavior - object[] arrays are not wrapped to maintain raw JSON format
        result[0].Should().BeOfType<JsonElement>().Subject.GetString().Should().Be("item1");
        result[1].Should().BeOfType<JsonElement>().Subject.GetString().Should().Be("item2");
        result[2].Should().BeOfType<JsonElement>().Subject.GetString().Should().Be("item3");
        
        // Verify it's not wrapped with type metadata (should be raw JSON array)
        serialized.Should().StartWith("[");
        serialized.Should().EndWith("]");
    }

    [Fact]
    public void SerializeDeserialize_JsonNode_DoesNotWrap()
    {
        // Arrange
        var converter = JsonDataConverter.Default;
        JsonNode input = JsonNode.Parse("""{"key": "value"}""")!;

        // Act
        string serialized = converter.Serialize(input);
        JsonNode? deserialized = converter.Deserialize<JsonNode>(serialized);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!["key"]!.GetValue<string>().Should().Be("value");
        
        // Verify it's not wrapped with type metadata
        serialized.Should().Contain("\"key\"");
        serialized.Should().Contain("\"value\"");
        serialized.Should().NotContain("$type");
    }
}

