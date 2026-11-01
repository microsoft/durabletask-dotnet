// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text;
using Azure;
using Azure.Core.Pipeline;
using Azure.Storage;
using Azure.Storage.Blobs;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests;

/// <summary>
/// Verifies v2 client selection with the actual Azure SDK pipeline and an offline HTTP handler.
/// </summary>
public class BlobPayloadStoreRoutingTests
{
    /// <summary>
    /// Both operations retain the configured SharedKey pipeline across containers within one account.
    /// </summary>
    [Theory]
    [InlineData("https://account.invalid/new", "https://account.invalid/old/folder/a%20b%252Fc", true)]
    [InlineData("https://account.invalid/new", "https://account.invalid/old/folder/a%20b%252Fc", false)]
    [InlineData("http://127.0.0.1:10000/accounta/new", "http://127.0.0.1:10000/accounta/old/blob", true)]
    [InlineData("http://127.0.0.1:10000/accounta/new", "http://127.0.0.1:10000/accounta/old/blob", false)]
    [InlineData("https://account.invalid/new", "https://account.invalid/new/blob", true)]
    [InlineData("https://account.invalid/new", "https://account.invalid/new/blob", false)]
    public async Task SameAccount_UsesConfiguredPipelineAsync(string configuredUri, string blobUri, bool delete)
    {
        // Arrange
        using RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        BlobClientOptions clientOptions = new() { Transport = new HttpClientTransport(httpClient) };
        BlobContainerClient container = new(
            new Uri(configuredUri),
            new StorageSharedKeyCredential("accounta", Convert.ToBase64String(new byte[64])),
            clientOptions);
        BlobPayloadStore store = new(new LargePayloadStorageOptions(), container);

        // Act
        if (delete)
        {
            (await store.DeleteAsync($"blob:v2:{blobUri}", CancellationToken.None))
                .Should().Be(PayloadDeleteOutcome.Deleted);
        }
        else
        {
            (await store.DownloadAsync($"blob:v2:{blobUri}", CancellationToken.None)).Should().Be("payload");
        }

        // Assert
        handler.Requests.Select(r => r.Method).Should().Equal(delete ? ["HEAD", "DELETE"] : ["GET"]);
        handler.Requests.Should().OnlyContain(r => r.Uri.GetLeftPart(UriPartial.Path) == blobUri);
        handler.Requests.Should().OnlyContain(r => r.AuthorizationScheme == "SharedKey");
        if (delete)
        {
            handler.Requests[1].IfMatch.Should().Be("\"owned-etag\"");
        }
    }

    /// <summary>
    /// A token for another endpoint or path-style account must never receive the configured account key.
    /// </summary>
    [Theory]
    [InlineData("https://account.invalid/new", "https://other.invalid/old/blob")]
    [InlineData("https://account.invalid/new", "http://account.invalid/old/blob")]
    [InlineData("https://account.invalid/new", "https://account.invalid:444/old/blob")]
    [InlineData("http://127.0.0.1:10000/accounta/new", "http://127.0.0.1:10000/accountb/old/blob")]
    public async Task DifferentAccountOrEndpoint_RejectsBeforeSendingAsync(string configuredUri, string blobUri)
    {
        // Arrange
        using RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        BlobContainerClient container = new(
            new Uri(configuredUri),
            new StorageSharedKeyCredential("accounta", Convert.ToBase64String(new byte[64])),
            new BlobClientOptions { Transport = new HttpClientTransport(httpClient) });
        BlobPayloadStore store = new(new LargePayloadStorageOptions(), container);

        // Act
        Func<Task> delete = () => store.DeleteAsync($"blob:v2:{blobUri}", CancellationToken.None);
        Func<Task> download = () => store.DownloadAsync($"blob:v2:{blobUri}", CancellationToken.None);

        // Assert
        await delete.Should().ThrowAsync<PayloadStorageException>();
        await download.Should().ThrowAsync<PayloadStorageException>();
        handler.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Reusing the pipeline preserves the original SAS and surfaces service permission failures unchanged.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SameAccount_SasDenied_PropagatesStorageFailureAsync(bool delete)
    {
        // Arrange - deliberately synthetic container-scoped SAS; the service decides whether it authorizes OLD.
        using RecordingHandler handler = new() { DenyAccess = true };
        using HttpClient httpClient = new(handler);
        BlobContainerClient container = new(
            new Uri("https://account.invalid/new?sv=2025-01-05&sr=c&sp=r&sig=fake-signature"),
            new BlobClientOptions { Transport = new HttpClientTransport(httpClient), Retry = { MaxRetries = 0 } });
        BlobPayloadStore store = new(new LargePayloadStorageOptions(), container);

        // Act
        Func<Task> operation = delete
            ? () => store.DeleteAsync("blob:v2:https://account.invalid/old/blob", CancellationToken.None)
            : () => store.DownloadAsync("blob:v2:https://account.invalid/old/blob", CancellationToken.None);
        RequestFailedException failure = await Assert.ThrowsAsync<RequestFailedException>(operation);

        // Assert
        failure.Status.Should().Be(403);
        failure.ErrorCode.Should().Be("AuthorizationPermissionMismatch");
        RecordedRequest request = handler.Requests.Should().ContainSingle().Subject;
        request.Uri.AbsolutePath.Should().Be("/old/blob");
        request.Uri.Query.Should().Contain("sr=c").And.Contain("sp=r").And.Contain("sig=fake-signature");
        request.AuthorizationScheme.Should().BeNull();
    }

    sealed record RecordedRequest(string Method, Uri Uri, string? AuthorizationScheme, string? IfMatch);

    sealed class RecordingHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = new();

        public bool DenyAccess { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Requests.Add(new(
                request.Method.Method, request.RequestUri!, request.Headers.Authorization?.Scheme,
                request.Headers.IfMatch.FirstOrDefault()?.ToString()));
            HttpResponseMessage response = new(this.DenyAccess
                ? HttpStatusCode.Forbidden
                : request.Method == HttpMethod.Delete ? HttpStatusCode.Accepted : HttpStatusCode.OK);
            response.Content = new StringContent(
                this.DenyAccess
                    ? "<Error><Code>AuthorizationPermissionMismatch</Code><Message>Denied by test service.</Message></Error>"
                    : "payload",
                Encoding.UTF8,
                this.DenyAccess ? "application/xml" : "text/plain");
            response.Headers.TryAddWithoutValidation("x-ms-request-id", "offline");
            if (this.DenyAccess)
            {
                response.Headers.TryAddWithoutValidation("x-ms-error-code", "AuthorizationPermissionMismatch");
            }
            else
            {
                response.Headers.ETag = new("\"owned-etag\"");
                response.Headers.TryAddWithoutValidation("x-ms-meta-managed_by", "dts");
                response.Headers.TryAddWithoutValidation("x-ms-blob-type", "BlockBlob");
                response.Content.Headers.LastModified = DateTimeOffset.UnixEpoch;
            }

            return Task.FromResult(response);
        }
    }
}
