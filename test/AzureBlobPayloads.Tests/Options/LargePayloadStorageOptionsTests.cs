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
}
