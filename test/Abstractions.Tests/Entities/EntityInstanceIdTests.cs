// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities;

namespace Microsoft.DurableTask.Abstractions.Tests.Entities;

public class EntityInstanceIdTests
{
    [Fact]
    public void TestValidEntityInstanceId()
    {
        var entityId = new EntityInstanceId("entity", "key1");
        Assert.Equal("@entity@key1", entityId.ToString());
    }

    [Fact]
    public void TestEntityNameLowercased()
    {
        var entityId = new EntityInstanceId("Entity", "key1");
        Assert.Equal("entity", entityId.Name);
        Assert.Equal("@entity@key1", entityId.ToString());
    }

    [Fact]
    public void TestEntityNameWithAtSymbolThrows()
    {
        Assert.Throws<ArgumentException>(() => new EntityInstanceId("entity@name", "key1"));
    }

    [Fact]
    public void TestEntityInstanceIdExceedsMaxLengthThrows()
    {
        string longName = new string('a', 50);
        string longKey = new string('b', 51);
        Assert.Throws<ArgumentException>(() => new EntityInstanceId(longName, longKey));
    }

    [Fact]
    public void TestEntityInstanceIdEqualsMaxLength()
    {
        string longName = new string('a', 49);
        string longKey = new string('b', 49);
        var entityId = new EntityInstanceId(longName, longKey);
        Assert.Equal($"@{longName}@{longKey}", entityId.ToString());
    }

    [Fact]
    public void TestFromStringValid()
    {
        var entityId = EntityInstanceId.FromString("@entity@key1");
        Assert.Equal("entity", entityId.Name);
        Assert.Equal("key1", entityId.Key);
    }

    [Fact]
    public void TestFromStringInvalidThrows()
    {
        Assert.Throws<ArgumentException>(() => EntityInstanceId.FromString("invalid"));
        Assert.Throws<ArgumentException>(() => EntityInstanceId.FromString("@entitykey1"));
        Assert.Throws<ArgumentException>(() => EntityInstanceId.FromString("@@key1"));
        Assert.Throws<ArgumentException>(() => EntityInstanceId.FromString($"@entity@{new string('a', 100)}"));
    }
}
