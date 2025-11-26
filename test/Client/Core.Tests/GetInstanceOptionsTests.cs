// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client.Tests;

public class GetInstanceOptionsTests
{
    [Fact]
    public void DefaultValues_AreFalse()
    {
        // Arrange & Act
        GetInstanceOptions options = new();

        // Assert
        options.GetInputsAndOutputs.Should().BeFalse();
        options.GetHistory.Should().BeFalse();
    }

    [Fact]
    public void GetInputsAndOutputs_CanBeSetToTrue()
    {
        // Arrange & Act
        GetInstanceOptions options = new() { GetInputsAndOutputs = true };

        // Assert
        options.GetInputsAndOutputs.Should().BeTrue();
        options.GetHistory.Should().BeFalse();
    }

    [Fact]
    public void GetHistory_CanBeSetToTrue()
    {
        // Arrange & Act
        GetInstanceOptions options = new() { GetHistory = true };

        // Assert
        options.GetInputsAndOutputs.Should().BeFalse();
        options.GetHistory.Should().BeTrue();
    }

    [Fact]
    public void BothOptions_CanBeSetToTrue()
    {
        // Arrange & Act
        GetInstanceOptions options = new()
        {
            GetInputsAndOutputs = true,
            GetHistory = true
        };

        // Assert
        options.GetInputsAndOutputs.Should().BeTrue();
        options.GetHistory.Should().BeTrue();
    }

    [Fact]
    public void Properties_CanBeModifiedAfterConstruction()
    {
        // Arrange
        GetInstanceOptions options = new();

        // Act
        options.GetInputsAndOutputs = true;
        options.GetHistory = true;

        // Assert
        options.GetInputsAndOutputs.Should().BeTrue();
        options.GetHistory.Should().BeTrue();
    }
}
