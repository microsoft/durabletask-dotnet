// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Entities.Tests;
using Xunit;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests.AutoPurge;

public class BlobPurgeJobTests
{
    readonly BlobPurgeJob job = new(new TestLogger<BlobPurgeJob>());

    [Fact]
    public async Task Create_WhenStopped_ActivatesJobAndStoresBatchSize()
    {
        // Arrange
        TestEntityOperation operation = new(
            nameof(BlobPurgeJob.Create),
            new TestEntityState(null),
            new BlobPurgeJobCreationOptions(250));

        // Act
        await this.job.RunAsync(operation);

        // Assert
        BlobPurgeJobState state = Assert.IsType<BlobPurgeJobState>(
            operation.State.GetState(typeof(BlobPurgeJobState)));
        state.Status.Should().Be(BlobPurgeJobStatus.Active);
        state.PurgeBatchSize.Should().Be(250);
        state.CreatedAt.Should().NotBeNull();
        state.LastModifiedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_WhenAlreadyActive_IsNoOp()
    {
        // Arrange
        BlobPurgeJobState existing = new()
        {
            Status = BlobPurgeJobStatus.Active,
            PurgeBatchSize = 100,
        };
        TestEntityOperation operation = new(
            nameof(BlobPurgeJob.Create),
            new TestEntityState(existing),
            new BlobPurgeJobCreationOptions(999));

        // Act
        await this.job.RunAsync(operation);

        // Assert - status stays Active and the original batch size is retained, proving the create no-op'd.
        BlobPurgeJobState state = Assert.IsType<BlobPurgeJobState>(
            operation.State.GetState(typeof(BlobPurgeJobState)));
        state.Status.Should().Be(BlobPurgeJobStatus.Active);
        state.PurgeBatchSize.Should().Be(100);
    }

    [Fact]
    public async Task Create_WithNonPositiveBatchSize_FallsBackToDefault()
    {
        // Arrange
        TestEntityOperation operation = new(
            nameof(BlobPurgeJob.Create),
            new TestEntityState(null),
            new BlobPurgeJobCreationOptions(0));

        // Act
        await this.job.RunAsync(operation);

        // Assert
        BlobPurgeJobState state = Assert.IsType<BlobPurgeJobState>(
            operation.State.GetState(typeof(BlobPurgeJobState)));
        state.PurgeBatchSize.Should().Be(500);
    }

    [Fact]
    public async Task Get_ReturnsCurrentState()
    {
        // Arrange
        BlobPurgeJobState existing = new()
        {
            Status = BlobPurgeJobStatus.Active,
            PurgeBatchSize = 42,
        };
        TestEntityOperation operation = new(
            nameof(BlobPurgeJob.Get),
            new TestEntityState(existing),
            null);

        // Act
        object? result = await this.job.RunAsync(operation);

        // Assert
        BlobPurgeJobState state = Assert.IsType<BlobPurgeJobState>(result);
        state.Status.Should().Be(BlobPurgeJobStatus.Active);
        state.PurgeBatchSize.Should().Be(42);
    }
}
