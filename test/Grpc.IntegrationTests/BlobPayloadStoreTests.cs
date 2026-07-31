// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Grpc.Tests;

public class BlobPayloadStoreTests
{
    [Fact]
    public void EncodeToken_FromBlobUri_ProducesSelfDescribingV2Token()
    {
        // Arrange
        Uri blobUri = new("https://myaccount.blob.core.windows.net/mycontainer/abc123def456");

        // Act
        string token = BlobPayloadStore.EncodeToken(blobUri);

        // Assert
        Assert.StartsWith("blob:v2:", token);
        Assert.Contains("mycontainer", token);
        Assert.Contains("abc123def456", token);

        // Round-trip: decoding the encoded token yields the original container and blob name.
        BlobPayloadStore.DecodeTokenResult decoded = BlobPayloadStore.DecodeToken(token);
        Assert.True(decoded.IsV2);
        Assert.Equal("mycontainer", decoded.Container);
        Assert.Equal("abc123def456", decoded.Name);
    }

    [Theory]
    [InlineData("https://myaccount.blob.core.windows.net/mycontainer/abc123def456", "mycontainer", "abc123def456")]
    [InlineData("http://127.0.0.1:10000/devstoreaccount1/mycontainer/abc123def456", "mycontainer", "abc123def456")]
    public void DecodeToken_V2Url_ParsesContainerAndBlob(string blobUrl, string expectedContainer, string expectedBlob)
    {
        // Arrange
        string token = "blob:v2:" + blobUrl;

        // Act
        BlobPayloadStore.DecodeTokenResult decoded = BlobPayloadStore.DecodeToken(token);

        // Assert
        Assert.True(decoded.IsV2);
        Assert.Equal(expectedContainer, decoded.Container);
        Assert.Equal(expectedBlob, decoded.Name);
        Assert.Equal(new Uri(blobUrl), decoded.BlobUri);
        Assert.NotNull(decoded.ContainerUri);
        Assert.EndsWith(expectedContainer, decoded.ContainerUri!.AbsolutePath);
    }

    [Fact]
    public void DecodeToken_LegacyV1Token_ParsesContainerAndBlob()
    {
        // Arrange
        string token = "blob:v1:mycontainer:abc123def456";

        // Act
        BlobPayloadStore.DecodeTokenResult decoded = BlobPayloadStore.DecodeToken(token);

        // Assert
        Assert.False(decoded.IsV2);
        Assert.Equal("mycontainer", decoded.Container);
        Assert.Equal("abc123def456", decoded.Name);
        Assert.Null(decoded.BlobUri);
        Assert.Null(decoded.ContainerUri);
    }

    [Fact]
    public void IsKnownPayloadToken_RecognizesV1AndV2_RejectsOtherValues()
    {
        // Arrange
        BlobPayloadStore store = CreateStore();

        // Act & Assert
        Assert.True(store.IsKnownPayloadToken("blob:v1:mycontainer:abc123"));
        Assert.True(store.IsKnownPayloadToken("blob:v2:https://acct.blob.core.windows.net/mycontainer/abc123"));
        Assert.False(store.IsKnownPayloadToken("just a normal payload"));
        Assert.False(store.IsKnownPayloadToken(string.Empty));
    }

    [Fact]
    public async Task DownloadAsync_V2TokenForDifferentAccountWithoutIdentity_ThrowsBeforeNetworkCall()
    {
        // Arrange: the configured store uses a connection string (account-key auth, no TokenCredential), while
        // the token points at a different storage account. Account keys are account-specific, so cross-account
        // reads are impossible and must fail fast (before any network call) with a clear error.
        BlobPayloadStore store = CreateStore();
        string token =
            "blob:v2:https://otheraccount.blob.core.windows.net/othercontainer/" + Guid.NewGuid().ToString("N");

        // Act
        PayloadStorageException ex = await Assert.ThrowsAsync<PayloadStorageException>(
            () => store.DownloadAsync(token, CancellationToken.None));

        // Assert
        Assert.Contains("different storage account", ex.Message);
    }

    static BlobPayloadStore CreateStore() =>
        new(new LargePayloadStorageOptions("UseDevelopmentStorage=true") { ContainerName = "test" });
}
