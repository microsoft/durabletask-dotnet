// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

public class TaskNameTests
{
    [Fact]
    public void Ctor_NullName_Throws()
    {
        Func<TaskName> act = () => new TaskName(null!);
        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("name");
    }

    [Fact]
    public void Ctor_EmptyName_Throws()
    {
        Func<TaskName> act = () => new TaskName(string.Empty);
        act.Should().ThrowExactly<ArgumentException>().WithParameterName("name");
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
