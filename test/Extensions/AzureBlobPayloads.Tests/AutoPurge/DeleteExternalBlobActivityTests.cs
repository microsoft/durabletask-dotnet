// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure;
using FluentAssertions;
using Microsoft.DurableTask.AzureBlobPayloads;
using Xunit;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests.AutoPurge;

public class DeleteExternalBlobActivityTests
{
    [Fact]
    public async Task RunAsync_WhenDeleteThrowsRequestFailed400_DiscardsPoisonToken()
    {
        // Arrange - a Status 400 (e.g. InvalidResourceName) is a permanent service rejection.
        StubPayloadStore store = new(new RequestFailedException(400, "InvalidResourceName"));
        DeleteExternalBlobActivity activity = new(store, new TestLogger<DeleteExternalBlobActivity>());

        // Act
        BlobDeleteResult result = await activity.RunAsync(null!, "blob:v1:payloads:bad name");

        // Assert - discarded so the backend acks and clears the row instead of re-streaming forever.
        result.Should().Be(BlobDeleteResult.Discarded);
    }

    [Fact]
    public async Task RunAsync_WhenDeleteThrowsRequestFailedNon400_LeavesTombstonedForRetry()
    {
        // Arrange - a Status 503 that escaped the SDK's internal retries is treated as transient.
        StubPayloadStore store = new(new RequestFailedException(503, "ServerBusy"));
        DeleteExternalBlobActivity activity = new(store, new TestLogger<DeleteExternalBlobActivity>());

        // Act
        BlobDeleteResult result = await activity.RunAsync(null!, "blob:v1:payloads:abc123");

        // Assert - left tombstoned so a later purge cycle can retry; a blob is never dropped on doubt.
        result.Should().Be(BlobDeleteResult.Retry);
    }

    sealed class StubPayloadStore : PayloadStore
    {
        readonly Exception? deleteError;

        public StubPayloadStore(Exception? deleteError) => this.deleteError = deleteError;

        public override Task DeleteAsync(string token, CancellationToken cancellationToken) =>
            this.deleteError is null ? Task.CompletedTask : throw this.deleteError;

        public override Task<string> UploadAsync(string payLoad, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<string> DownloadAsync(string token, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override bool IsKnownPayloadToken(string value) => true;
    }
}
