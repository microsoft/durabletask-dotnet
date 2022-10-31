// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

public class TaskNameTests
{
    [Fact]
    public void Ctor_NullName_Default()
    {
        TaskName name = new(null!);
        name.Should().Be(default(TaskName));
    }

    [Fact]
    public void Ctor_EmptyName_Okay()
    {
        TaskName name = new(string.Empty);
        name.Name.Should().Be(string.Empty);
        name.Version.Should().Be(string.Empty);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Conversion_EqualToDefault(string? str)
    {
        TaskName name = str!;
        name.Should().Be(default(TaskName));
    }

    [Fact]
    public void Equals_SameName_Equal()
    {
        string str = Guid.NewGuid().ToString();
        TaskName left = new(str);
        TaskName right = str;
        left.Equals(right).Should().BeTrue();
        left.Equals((object)right).Should().BeTrue();
        (left == right).Should().BeTrue();
        (left != right).Should().BeFalse();
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentName_NotEqual()
    {
        TaskName left = new(Guid.NewGuid().ToString());
        TaskName right = Guid.NewGuid().ToString();
        left.Equals(right).Should().BeFalse();
        left.Equals((object)right).Should().BeFalse();
        (left == right).Should().BeFalse();
        (left != right).Should().BeTrue();
        left.GetHashCode().Should().NotBe(right.GetHashCode());
    }
}
