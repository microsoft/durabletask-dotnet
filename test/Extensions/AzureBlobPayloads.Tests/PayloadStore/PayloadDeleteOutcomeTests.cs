// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests;

/// <summary>
/// Pins the public outcome values so default-initialized results cannot imply successful deletion.
/// </summary>
public class PayloadDeleteOutcomeTests
{
    /// <summary>
    /// The default value represents an unconfirmed outcome, not a terminal success.
    /// </summary>
    [Fact]
    public void Default_IsUnspecified()
    {
        // Arrange
        PayloadDeleteOutcome outcome = default;

        // Act
        string name = outcome.ToString();

        // Assert
        name.Should().Be("Unspecified");
    }

    /// <summary>
    /// Successful outcomes have explicit, nonzero public values.
    /// </summary>
    [Theory]
    [InlineData(PayloadDeleteOutcome.Deleted, 1)]
    [InlineData(PayloadDeleteOutcome.AlreadyAbsent, 2)]
    [InlineData(PayloadDeleteOutcome.NotStoreOwned, 3)]
    public void TerminalOutcome_HasExplicitValue(PayloadDeleteOutcome outcome, int expectedValue)
    {
        // Arrange
        PayloadDeleteOutcome terminalOutcome = outcome;

        // Act
        int value = (int)terminalOutcome;

        // Assert
        value.Should().Be(expectedValue);
    }
}
