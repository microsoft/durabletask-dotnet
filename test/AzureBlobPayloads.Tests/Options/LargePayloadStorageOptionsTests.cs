// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Xunit;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests.Options;

public class LargePayloadStorageOptionsTests
{
    [Fact]
    public void Defaults_AutoPurgeDisabled_AndBatchSize500()
    {
        // Arrange & Act
        LargePayloadStorageOptions options = new();

        // Assert
        options.AutoPurge.Should().BeFalse();
        options.PayloadPurgeBatchSize.Should().Be(500);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000)]
    [InlineData(1001)]
    public void PayloadPurgeBatchSize_OutOfRange_Throws(int value)
    {
        // Arrange
        LargePayloadStorageOptions options = new();

        // Act
        Action act = () => options.PayloadPurgeBatchSize = value;

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(500)]
    [InlineData(999)]
    public void PayloadPurgeBatchSize_InRange_IsAccepted(int value)
    {
        // Arrange
        LargePayloadStorageOptions options = new();

        // Act
        options.PayloadPurgeBatchSize = value;

        // Assert
        options.PayloadPurgeBatchSize.Should().Be(value);
    }
}
