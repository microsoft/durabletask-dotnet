// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text;
using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Storage;
using Azure.Storage.Blobs;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Client;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests;

/// <summary>
/// Verifies v2 client selection with the actual Azure SDK pipeline and an offline HTTP handler.
/// </summary>
public class BlobPayloadStoreRoutingTests
{
    /// <summary>
    /// Downloads and deletion retain the configured SharedKey pipeline within the configured container.
    /// </summary>
    [Theory]
    [InlineData("https://account.invalid/new", "blob:v2:https://account.invalid/new/folder/a%20b%252Fc", "https://account.invalid/new/folder/a%20b%252Fc", false)]
    [InlineData("https://account.invalid/new", "blob:v2:https://account.invalid/new/folder/a%20b%252Fc", "https://account.invalid/new/folder/a%20b%252Fc", true)]
    [InlineData("https://account.invalid/new", "blob:v1:new:folder/a b%2Fc", "https://account.invalid/new/folder/a%20b%252Fc", false)]
    [InlineData("https://account.invalid/new", "blob:v1:new:folder/a b%2Fc", "https://account.invalid/new/folder/a%20b%252Fc", true)]
    [InlineData("http://127.0.0.1:10000/accounta/new", "blob:v2:http://127.0.0.1:10000/accounta/new/blob", "http://127.0.0.1:10000/accounta/new/blob", false)]
    [InlineData("http://127.0.0.1:10000/accounta/new", "blob:v2:http://127.0.0.1:10000/accounta/new/blob", "http://127.0.0.1:10000/accounta/new/blob", true)]
    [InlineData("http://127.0.0.1:10000/accounta/new", "blob:v1:new:blob", "http://127.0.0.1:10000/accounta/new/blob", false)]
    [InlineData("http://127.0.0.1:10000/accounta/new", "blob:v1:new:blob", "http://127.0.0.1:10000/accounta/new/blob", true)]
    public async Task ConfiguredContainer_UsesConfiguredPipelineAsync(
        string configuredUri, string token, string blobUri, bool delete)
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
        if (delete)
        {
            (await store.DeleteAsync(token, CancellationToken.None)).Should().Be(PayloadDeleteOutcome.Deleted);
        }
        else
        {
            (await store.DownloadAsync(token, CancellationToken.None)).Should().Be("payload");
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
    /// Downloads and deletion outside the configured container fail before storage or identity access.
    /// </summary>
    [Theory]
    [InlineData("https://account.invalid/new", "https://account.invalid/old/blob")]
    [InlineData("https://account.invalid/new", "https://account.invalid/new-other/blob")]
    [InlineData("https://account.invalid/new", "https://other.invalid/new/blob")]
    [InlineData("https://account.invalid/new", "http://account.invalid/new/blob")]
    [InlineData("https://account.invalid/new", "https://account.invalid:444/new/blob")]
    [InlineData("http://127.0.0.1:10000/accounta/new", "http://127.0.0.1:10000/accountb/new/blob")]
    [InlineData("https://account.invalid/new", "https://account.invalid/new/../old/blob")]
    [InlineData("https://account.invalid/new", "https://account.invalid/new/%2e%2e/old/blob")]
    public async Task OtherContainer_RejectsBeforeSendingAsync(string configuredUri, string blobUri)
    {
        // Arrange
        using RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        Mock<TokenCredential> credential = new(MockBehavior.Strict);
        BlobContainerClient container = new(
            new Uri(configuredUri),
            new StorageSharedKeyCredential("accounta", Convert.ToBase64String(new byte[64])),
            new BlobClientOptions { Transport = new HttpClientTransport(httpClient) });
        BlobPayloadStore store = new(new LargePayloadStorageOptions { Credential = credential.Object }, container);

        // Act
        Func<Task> download = () => store.DownloadAsync($"blob:v2:{blobUri}", CancellationToken.None);
        Func<Task> delete = () => store.DeleteAsync($"blob:v2:{blobUri}", CancellationToken.None);

        // Assert
        await download.Should().ThrowAsync<PayloadStorageException>()
            .WithMessage("*configured payload container*");
        await delete.Should().ThrowAsync<PayloadStorageException>()
            .WithMessage("*configured payload container*");
        handler.Requests.Should().BeEmpty();
        credential.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Legacy blob names must also remain within the configured container after URI normalization.
    /// </summary>
    [Theory]
    [InlineData("../old/blob")]
    [InlineData("folder/../../old/blob")]
    public async Task LegacyToken_OutsideContainerAfterNormalization_RejectsBeforeSendingAsync(string blobName)
    {
        // Arrange
        using RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        BlobContainerClient container = new(
            new Uri("https://account.invalid/new"),
            new StorageSharedKeyCredential("accounta", Convert.ToBase64String(new byte[64])),
            new BlobClientOptions { Transport = new HttpClientTransport(httpClient) });
        BlobPayloadStore store = new(new LargePayloadStorageOptions(), container);

        // Act
        Func<Task> download = () => store.DownloadAsync($"blob:v1:new:{blobName}", CancellationToken.None);
        Func<Task> delete = () => store.DeleteAsync($"blob:v1:new:{blobName}", CancellationToken.None);

        // Assert
        await download.Should().ThrowAsync<PayloadStorageException>();
        await delete.Should().ThrowAsync<PayloadStorageException>();
        handler.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Purge preserves an out-of-store payload's tombstone for a store configured for its location.
    /// </summary>
    [Fact]
    public async Task Purge_OtherContainer_RetriesWithoutStorageRequestsAsync()
    {
        // Arrange
        using RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        BlobContainerClient container = new(
            new Uri("https://account.invalid/new"),
            new StorageSharedKeyCredential("accounta", Convert.ToBase64String(new byte[64])),
            new BlobClientOptions { Transport = new HttpClientTransport(httpClient) });
        BlobPayloadStore store = new(new LargePayloadStorageOptions(), container);
        TestLogger<DeleteExternalBlobActivity> logger = new();
        DeleteExternalBlobActivity activity = new(store, logger);

        // Act
        List<BlobPurgeOutcome> outcomes = await activity.RunAsync(
            null!, ["blob:v2:https://account.invalid/old/blob"]);

        // Assert
        outcomes.Should().ContainSingle().Which.Disposition.Should().Be(LargePayloadPurgeDisposition.Retry);
        handler.Requests.Should().BeEmpty();
        logger.Logs.Should().ContainSingle(l => l.Message.Contains("PayloadStoreUnavailable"));
    }

    /// <summary>
    /// Work-item input resolution enforces the store's configured container before exposing the work item.
    /// </summary>
    [Theory]
    [InlineData("new", true)]
    [InlineData("old", false)]
    public async Task WorkItem_ResolvesInputOnlyFromConfiguredContainerAsync(string tokenContainer, bool resolves)
    {
        // Arrange
        using RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        BlobContainerClient container = new(
            new Uri("https://account.invalid/new"),
            new StorageSharedKeyCredential("accounta", Convert.ToBase64String(new byte[64])),
            new BlobClientOptions { Transport = new HttpClientTransport(httpClient) });
        BlobPayloadStore store = new(new LargePayloadStorageOptions(), container);
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, new LargePayloadStorageOptions());
        string token = $"blob:v2:https://account.invalid/{tokenContainer}/blob";
        P.WorkItem workItem = new()
        {
            OrchestratorRequest = new P.OrchestratorRequest
            {
                NewEvents =
                {
                    new P.HistoryEvent { ExecutionStarted = new P.ExecutionStartedEvent { Input = token } },
                },
            },
        };
        Mock<IAsyncStreamReader<P.WorkItem>> reader = new();
        reader.Setup(r => r.MoveNext(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        reader.SetupGet(r => r.Current).Returns(workItem);
        Method<P.GetWorkItemsRequest, P.WorkItem> method = new(
            MethodType.ServerStreaming, "TaskHubSidecarService", "GetWorkItems",
            Marshallers.Create<P.GetWorkItemsRequest>(_ => [], _ => new()),
            Marshallers.Create<P.WorkItem>(_ => [], _ => new()));
        using AsyncServerStreamingCall<P.WorkItem> call = interceptor.AsyncServerStreamingCall(
            new P.GetWorkItemsRequest(),
            new ClientInterceptorContext<P.GetWorkItemsRequest, P.WorkItem>(method, null, default),
            (_, _) => new AsyncServerStreamingCall<P.WorkItem>(
                reader.Object, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        // Act
        Func<Task<bool>> moveNext = () => call.ResponseStream.MoveNext(CancellationToken.None);

        // Assert
        if (resolves)
        {
            (await moveNext()).Should().BeTrue();
            call.ResponseStream.Current.OrchestratorRequest.NewEvents[0].ExecutionStarted.Input.Should().Be("payload");
            handler.Requests.Should().ContainSingle();
        }
        else
        {
            await moveNext.Should().ThrowAsync<PayloadStorageException>();
            workItem.OrchestratorRequest.NewEvents[0].ExecutionStarted.Input.Should().Be(token);
            handler.Requests.Should().BeEmpty();
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
        // Arrange - deliberately synthetic container-scoped SAS; storage decides whether it grants access.
        using RecordingHandler handler = new() { DenyAccess = true };
        using HttpClient httpClient = new(handler);
        BlobContainerClient container = new(
            new Uri("https://account.invalid/new?sv=2025-01-05&sr=c&sp=r&sig=fake-signature"),
            new BlobClientOptions { Transport = new HttpClientTransport(httpClient), Retry = { MaxRetries = 0 } });
        BlobPayloadStore store = new(new LargePayloadStorageOptions(), container);

        // Act
        Func<Task> operation = delete
            ? () => store.DeleteAsync("blob:v2:https://account.invalid/new/blob", CancellationToken.None)
            : () => store.DownloadAsync("blob:v2:https://account.invalid/new/blob", CancellationToken.None);
        RequestFailedException failure = await Assert.ThrowsAsync<RequestFailedException>(operation);

        // Assert
        failure.Status.Should().Be(403);
        failure.ErrorCode.Should().Be("AuthorizationPermissionMismatch");
        RecordedRequest request = handler.Requests.Should().ContainSingle().Subject;
        request.Uri.AbsolutePath.Should().Be("/new/blob");
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
