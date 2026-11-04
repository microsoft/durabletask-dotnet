// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Net.Sockets;
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
    [InlineData("https://account.invalid/new", "blob:v2:https://ACCOUNT.invalid:443/new/blob", "https://account.invalid/new/blob", false)]
    [InlineData("https://account.invalid/new", "blob:v2:https://ACCOUNT.invalid:443/new/blob", "https://account.invalid/new/blob", true)]
    [InlineData("https://account.invalid/new", "blob:v2:https://account.invalid/new/blob%23name%3Fpart", "https://account.invalid/new/blob%23name%3Fpart", false)]
    [InlineData("https://account.invalid/new", "blob:v2:https://account.invalid/new/blob%23name%3Fpart", "https://account.invalid/new/blob%23name%3Fpart", true)]
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
    [InlineData("https://account.invalid/new", "https://account.invalid/new%2f..%2fold/blob")]
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
            .WithMessage("*container*");
        await delete.Should().ThrowAsync<PayloadStorageException>()
            .WithMessage("*container*");
        handler.Requests.Should().BeEmpty();
        credential.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Legacy blob names must retain their identity and remain within the configured container.
    /// </summary>
    [Theory]
    [InlineData("../old/blob", false, false)]
    [InlineData("../old/blob", true, false)]
    [InlineData("folder/../../old/blob", false, false)]
    [InlineData("folder/../../old/blob", true, false)]
    [InlineData("folder/../blob", false, true)]
    [InlineData("folder/../blob", true, true)]
    [InlineData("folder/./blob", false, true)]
    [InlineData("folder/./blob", true, true)]
    public async Task LegacyToken_ChangedByNormalization_RejectsBeforeSendingAsync(
        string blobName, bool allowAllContainers, bool remainsInContainer)
    {
        // Arrange
        using RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        BlobContainerClient container = new(
            new Uri("https://account.invalid/new"),
            new StorageSharedKeyCredential("accounta", Convert.ToBase64String(new byte[64])),
            new BlobClientOptions { Transport = new HttpClientTransport(httpClient) });
        BlobPayloadStore store = new(
            new LargePayloadStorageOptions { AllowAllContainersInConfiguredAccount = allowAllContainers }, container);

        // Act
        Func<Task> download = () => store.DownloadAsync($"blob:v1:new:{blobName}", CancellationToken.None);
        Func<Task> delete = () => store.DeleteAsync($"blob:v1:new:{blobName}", CancellationToken.None);

        // Assert
        if (remainsInContainer)
        {
            await download.Should().ThrowAsync<ArgumentException>().WithMessage("*blob name*normalization*");
            await delete.Should().ThrowAsync<ArgumentException>().WithMessage("*blob name*normalization*");
        }
        else
        {
            await download.Should().ThrowAsync<PayloadStorageException>();
            await delete.Should().ThrowAsync<PayloadStorageException>();
        }

        handler.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Purge preserves an out-of-store payload's tombstone for a store configured for its location.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Purge_UsesConfiguredAccountScopeAsync(bool allowAllContainers)
    {
        // Arrange
        using RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        BlobContainerClient container = new(
            new Uri("https://account.invalid/new"),
            new StorageSharedKeyCredential("accounta", Convert.ToBase64String(new byte[64])),
            new BlobClientOptions { Transport = new HttpClientTransport(httpClient) });
        BlobPayloadStore store = new(
            new LargePayloadStorageOptions { AllowAllContainersInConfiguredAccount = allowAllContainers }, container);
        TestLogger<DeleteExternalBlobActivity> logger = new();
        DeleteExternalBlobActivity activity = new(store, logger);

        // Act
        List<BlobPurgeOutcome> outcomes = await activity.RunAsync(
            null!, ["blob:v2:https://account.invalid/old/blob"]);

        // Assert
        if (allowAllContainers)
        {
            outcomes.Should().ContainSingle().Which.Disposition.Should().Be(LargePayloadPurgeDisposition.Deleted);
            handler.Requests.Select(r => r.Method).Should().Equal("HEAD", "DELETE");
            handler.Requests[1].IfMatch.Should().Be("\"owned-etag\"");
        }
        else
        {
            outcomes.Should().ContainSingle().Which.Disposition.Should().Be(LargePayloadPurgeDisposition.Retry);
            handler.Requests.Should().BeEmpty();
            logger.Logs.Should().ContainSingle(l => l.Message.Contains("PayloadStoreUnavailable"));
        }
    }

    /// <summary>
    /// Work-item input, event, and past-result resolution use the same configured access scope.
    /// </summary>
    [Theory]
    [InlineData("new", false, true)]
    [InlineData("old", false, false)]
    [InlineData("old", true, true)]
    public async Task WorkItem_ResolvesHistoryUsingConfiguredScopeAsync(
        string tokenContainer, bool allowAllContainers, bool resolves)
    {
        // Arrange
        using RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        BlobContainerClient container = new(
            new Uri("https://account.invalid/new"),
            new StorageSharedKeyCredential("accounta", Convert.ToBase64String(new byte[64])),
            new BlobClientOptions { Transport = new HttpClientTransport(httpClient) });
        BlobPayloadStore store = new(
            new LargePayloadStorageOptions { AllowAllContainersInConfiguredAccount = allowAllContainers }, container);
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, new LargePayloadStorageOptions());
        string token = $"blob:v2:https://account.invalid/{tokenContainer}/blob";
        P.WorkItem workItem = new()
        {
            OrchestratorRequest = new P.OrchestratorRequest
            {
                PastEvents =
                {
                    new P.HistoryEvent { TaskCompleted = new P.TaskCompletedEvent { Result = token } },
                },
                NewEvents =
                {
                    new P.HistoryEvent { ExecutionStarted = new P.ExecutionStartedEvent { Input = token } },
                    new P.HistoryEvent { EventRaised = new P.EventRaisedEvent { Input = token } },
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
            call.ResponseStream.Current.OrchestratorRequest.NewEvents[1].EventRaised.Input.Should().Be("payload");
            call.ResponseStream.Current.OrchestratorRequest.PastEvents[0].TaskCompleted.Result.Should().Be("payload");
            handler.Requests.Should().HaveCount(3);
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

    /// <summary>
    /// Account-wide access retains the configured authentication pipeline for v1 and v2 references.
    /// </summary>
    [Theory]
    [InlineData("https://account.invalid/new", "blob:v2:https://account.invalid/old/folder/a%20b%252Fc", "/old/folder/a%20b%252Fc")]
    [InlineData("https://account.invalid/new", "blob:v1:old:folder/a b%2Fc", "/old/folder/a%20b%252Fc")]
    [InlineData("http://127.0.0.1:10000/accounta/new", "blob:v2:http://127.0.0.1:10000/accounta/old/blob", "/accounta/old/blob")]
    [InlineData("http://127.0.0.1:10000/accounta/new", "blob:v1:old:blob", "/accounta/old/blob")]
    [InlineData("https://account.invalid/new", "blob:v2:https://ACCOUNT.invalid:443/old/blob", "/old/blob")]
    [InlineData("https://account.invalid/new", "blob:v2:https://account.invalid/new/../old/blob", "/old/blob")]
    [InlineData("https://account.invalid/new", "blob:v2:https://account.invalid/new/%2e%2e/old/blob", "/old/blob")]
    public async Task AccountScope_UsesConfiguredPipelineAsync(string configuredUri, string token, string expectedPath)
    {
        // Arrange
        using RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        BlobContainerClient container = new(
            new Uri(configuredUri),
            new StorageSharedKeyCredential("accounta", Convert.ToBase64String(new byte[64])),
            new BlobClientOptions { Transport = new HttpClientTransport(httpClient) });
        BlobPayloadStore store = new(new LargePayloadStorageOptions { AllowAllContainersInConfiguredAccount = true }, container);

        // Act
        string payload = await store.DownloadAsync(token, CancellationToken.None);
        PayloadDeleteOutcome outcome = await store.DeleteAsync(token, CancellationToken.None);

        // Assert
        payload.Should().Be("payload");
        outcome.Should().Be(PayloadDeleteOutcome.Deleted);
        handler.Requests.Select(r => r.Method).Should().Equal("GET", "HEAD", "DELETE");
        handler.Requests.Should().OnlyContain(r => r.Uri.AbsolutePath == expectedPath && r.AuthorizationScheme == "SharedKey");
        handler.Requests[2].IfMatch.Should().Be("\"owned-etag\"");
    }

    /// <summary>
    /// Account-wide access never relaxes the configured service endpoint or path-style account boundary.
    /// </summary>
    [Theory]
    [InlineData("https://account.invalid/new", "https://other.invalid/new/blob")]
    [InlineData("https://account.invalid/new", "http://account.invalid/new/blob")]
    [InlineData("https://account.invalid/new", "https://account.invalid:444/new/blob")]
    [InlineData("http://127.0.0.1:10000/accounta/new", "http://127.0.0.1:10000/accountb/new/blob")]
    [InlineData("http://127.0.0.1:10000/accounta/new", "http://127.0.0.1:10000/accounta/../accountb/new/blob")]
    [InlineData("http://127.0.0.1:10000/accounta/new", "http://127.0.0.1:10000/accounta/%2e%2e/accountb/new/blob")]
    [InlineData("http://127.0.0.1:10000/accounta/new", "http://127.0.0.1:10000/accounta%2f..%2faccountb/new/blob")]
    public async Task AccountScope_OtherEndpointRejectsBeforeSendingAsync(string configuredUri, string blobUri)
    {
        // Arrange
        using RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        Mock<TokenCredential> credential = new(MockBehavior.Strict);
        BlobContainerClient container = new(
            new Uri(configuredUri),
            new StorageSharedKeyCredential("accounta", Convert.ToBase64String(new byte[64])),
            new BlobClientOptions { Transport = new HttpClientTransport(httpClient) });
        BlobPayloadStore store = new(
            new LargePayloadStorageOptions { AllowAllContainersInConfiguredAccount = true, Credential = credential.Object },
            container);
        string token = $"blob:v2:{blobUri}";

        // Act
        Func<Task> download = () => store.DownloadAsync(token, CancellationToken.None);
        Func<Task> delete = () => store.DeleteAsync(token, CancellationToken.None);

        // Assert
        await download.Should().ThrowAsync<PayloadStorageException>();
        await delete.Should().ThrowAsync<PayloadStorageException>();
        handler.Requests.Should().BeEmpty();
        credential.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Path-style account boundaries also hold when a legacy container name is normalized by the client.
    /// </summary>
    [Theory]
    [InlineData("blob:v1:../accountb/old:blob")]
    [InlineData("blob:v1:old/../../accountb/old:blob")]
    [InlineData("blob:v2:http://127.0.0.1:10000/accounta/%2e%2e%2faccountb%2fold/blob")]
    [InlineData("blob:v2:http://127.0.0.1:10000/accounta/%252e%252e%252faccountb%252fold/blob")]
    [InlineData("blob:v1:..%2faccountb%2fold:blob")]
    public async Task AccountScope_NormalizedContainerCannotChangeAccountAsync(string token)
    {
        // Arrange
        using RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        BlobContainerClient container = new(
            new Uri("http://127.0.0.1:10000/accounta/new"),
            new StorageSharedKeyCredential("accounta", Convert.ToBase64String(new byte[64])),
            new BlobClientOptions { Transport = new HttpClientTransport(httpClient) });
        BlobPayloadStore store = new(new LargePayloadStorageOptions { AllowAllContainersInConfiguredAccount = true }, container);

        // Act
        Func<Task> download = () => store.DownloadAsync(token, CancellationToken.None);
        Func<Task> delete = () => store.DeleteAsync(token, CancellationToken.None);

        // Assert
        await download.Should().ThrowAsync<PayloadStorageException>();
        await delete.Should().ThrowAsync<PayloadStorageException>();
        handler.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// An additional-account scope uses the configured identity for all containers in that exact endpoint.
    /// </summary>
    [Theory]
    [InlineData("https://other.invalid", "https://other.invalid/old/folder/a%20b%252Fc", "https://other.invalid/old/folder/a%20b%252Fc")]
    [InlineData("https://other.invalid/", "https://OTHER.invalid:443/another/blob", "https://other.invalid/another/blob")]
    [InlineData("https://other.invalid:444", "https://other.invalid:444/old/blob", "https://other.invalid:444/old/blob")]
    [InlineData("https://127.0.0.1:10000/accountb", "https://127.0.0.1:10000/accountb/old/blob", "https://127.0.0.1:10000/accountb/old/blob")]
    [InlineData("https://other.invalid", "https://other.invalid/new/%2e%2e/old/blob", "https://other.invalid/old/blob")]
    [InlineData("https://other.invalid", "https://other.invalid/old/blob?sv=2025-01-05&sig=from-token#unused", "https://other.invalid/old/blob")]
    [InlineData("https://other.invalid", "https://other.invalid/old/blob?snapshot=old&versionid=old#unused", "https://other.invalid/old/blob")]
    [InlineData("https://other.invalid", "https://other.invalid/old/folder/.hidden", "https://other.invalid/old/folder/.hidden")]
    [InlineData("https://other.invalid", "https://other.invalid/old/folder/a..b", "https://other.invalid/old/folder/a..b")]
    [InlineData("https://other.invalid", "https://other.invalid/old/folder/.../blob", "https://other.invalid/old/folder/.../blob")]
    [InlineData("https://other.invalid", "https://other.invalid/old/folder/%252e%252e/blob", "https://other.invalid/old/folder/%252e%252e/blob")]
    [InlineData("https://other.invalid", "https://other.invalid/old/folder%2fchild%2fblob", "https://other.invalid/old/folder/child/blob")]
    public async Task AdditionalAccount_UsesConfiguredIdentityAsync(
        string additionalAccount, string blobUri, string expectedBlobUri)
    {
        // Arrange
        using RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        Mock<TokenCredential> credential = CreateCredential();
        LargePayloadStorageOptions options = new(new Uri("https://account.invalid"), credential.Object);
        options.AdditionalAccountUris.Add(new Uri(additionalAccount));
        BlobClientOptions clientOptions = new() { Transport = new HttpClientTransport(httpClient) };
        BlobContainerClient container = new(new Uri("https://account.invalid/new"), credential.Object, clientOptions);
        BlobPayloadStore store = new(options, container, clientOptions);
        string token = $"blob:v2:{blobUri}";

        // Act
        string payload = await store.DownloadAsync(token, CancellationToken.None);
        PayloadDeleteOutcome outcome = await store.DeleteAsync(token, CancellationToken.None);

        // Assert
        payload.Should().Be("payload");
        outcome.Should().Be(PayloadDeleteOutcome.Deleted);
        handler.Requests.Select(r => r.Method).Should().Equal("GET", "HEAD", "DELETE");
        handler.Requests.Should().OnlyContain(
            r => r.Uri.GetLeftPart(UriPartial.Path) == expectedBlobUri
                && r.AuthorizationScheme == "Bearer");
        handler.Requests[2].IfMatch.Should().Be("\"owned-etag\"");
        handler.Requests.Should().OnlyContain(r => r.Uri.Query.Length == 0 && r.Uri.Fragment.Length == 0);
        credential.Verify(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    /// <summary>
    /// Reads and deletion preserve distinct objects with leading or trailing slashes in their names.
    /// </summary>
    [Theory]
    [InlineData("https://account.invalid", "new", false, false, false)]
    [InlineData("https://account.invalid", "old", true, false, false)]
    [InlineData("https://other.invalid", "old", false, true, false)]
    [InlineData("https://127.0.0.1:10000/accountb", "old", false, true, false)]
    [InlineData("https://account.invalid", "new", false, false, true)]
    [InlineData("https://account.invalid", "old", true, false, true)]
    public async Task BlobNameSlashes_PreserveObjectIdentityAsync(
        string accountUri, string tokenContainer, bool allowPrimary, bool additional, bool legacy)
    {
        (string Name, string Payload)[] blobNames =
        [
            ("blob", "plain"),
            ("/blob", "leading"),
            ("blob/", "trailing"),
        ];
        foreach ((string blobName, string expectedPayload) in blobNames)
        {
            // Arrange
            string containerUri = $"{accountUri}/{tokenContainer}";
            Dictionary<string, string> blobs = new(StringComparer.Ordinal)
            {
                [$"{containerUri}/blob"] = "plain",
                [$"{containerUri}//blob"] = "leading",
                [$"{containerUri}/blob/"] = "trailing",
            };
            string expectedUri = $"{containerUri}/{blobName}";
            KeyValuePair<string, string>[] remainingBlobs = blobs.Where(b => b.Key != expectedUri).ToArray();
            using RecordingHandler handler = new() { BlobPayloads = blobs };
            using HttpClient httpClient = new(handler);
            Mock<TokenCredential> credential = CreateCredential();
            LargePayloadStorageOptions options = new(new Uri("https://account.invalid"), credential.Object)
            {
                AllowAllContainersInConfiguredAccount = allowPrimary,
            };
            if (additional)
            {
                options.AdditionalAccountUris.Add(new Uri(accountUri));
            }

            BlobClientOptions clientOptions = new()
            {
                Transport = new HttpClientTransport(httpClient),
                TrimBlobNameSlashes = false,
            };
            BlobContainerClient container = new(new Uri("https://account.invalid/new"), credential.Object, clientOptions);
            BlobPayloadStore store = new(options, container, clientOptions);
            string token = legacy ? $"blob:v1:{tokenContainer}:{blobName}" : $"blob:v2:{expectedUri}";

            // Act
            string payload = await store.DownloadAsync(token, CancellationToken.None);
            PayloadDeleteOutcome outcome = await store.DeleteAsync(token, CancellationToken.None);

            // Assert
            payload.Should().Be(expectedPayload);
            outcome.Should().Be(PayloadDeleteOutcome.Deleted);
            blobs.Should().BeEquivalentTo(remainingBlobs.ToDictionary(b => b.Key, b => b.Value));
            handler.Requests.Select(r => r.Method).Should().Equal("GET", "HEAD", "DELETE");
            handler.Requests.Should().OnlyContain(
                r => r.Uri.GetLeftPart(UriPartial.Path) == expectedUri && r.AuthorizationScheme == "Bearer");
            handler.Requests[2].IfMatch.Should().Be($"\"{expectedPayload}-etag\"");
        }
    }

    /// <summary>
    /// Decoded names that normalize to another object are rejected without reading or deleting either object.
    /// </summary>
    [Theory]
    [InlineData("https://account.invalid/new/folder%2f..%2fblob", false, false)]
    [InlineData("https://account.invalid/new/folder%2f..%2fblob", true, false)]
    [InlineData("https://account.invalid/old/folder%2f..%2fblob", false, true)]
    [InlineData("https://account.invalid/old/folder%2f..%2fblob", true, true)]
    [InlineData("https://archive.invalid/old/folder%2f..%2fblob", false, false)]
    [InlineData("https://archive.invalid/old/folder%2f..%2fblob", true, false)]
    [InlineData("https://127.0.0.1:10000/accountb/old/folder%2f..%2fblob", false, false)]
    [InlineData("https://127.0.0.1:10000/accountb/old/folder%2f..%2fblob", true, false)]
    [InlineData("https://archive.invalid/old/folder%2f.%2fblob", false, false)]
    [InlineData("https://archive.invalid/old/folder%2f.%2fblob", true, false)]
    [InlineData("https://archive.invalid/old/folder%2f%2e%2e%2fblob", false, false)]
    [InlineData("https://archive.invalid/old/folder%2f%2e%2e%2fblob", true, false)]
    [InlineData("https://archive.invalid/old/folder%2f%2e%2fblob", false, false)]
    [InlineData("https://archive.invalid/old/folder%2f%2e%2fblob", true, false)]
    [InlineData("https://archive.invalid/old//folder%2f..%2fblob/", false, false)]
    [InlineData("https://archive.invalid/old//folder%2f..%2fblob/", true, false)]
    public async Task BlobNameNormalization_RejectsBeforeSendingAsync(
        string blobUri, bool delete, bool allowPrimary)
    {
        // Arrange
        string referencedUri = new Uri(blobUri).GetLeftPart(UriPartial.Path);
        string normalizedUri = new Uri(Uri.UnescapeDataString(blobUri)).GetLeftPart(UriPartial.Path);
        Dictionary<string, string> blobs = new(StringComparer.Ordinal)
        {
            [referencedUri] = "referenced",
            [normalizedUri] = "normalized",
        };
        Dictionary<string, string> expectedBlobs = new(blobs);
        using RecordingHandler handler = new() { BlobPayloads = blobs };
        using HttpClient httpClient = new(handler);
        Mock<TokenCredential> credential = CreateCredential();
        LargePayloadStorageOptions options = new(new Uri("https://account.invalid"), credential.Object)
        {
            AllowAllContainersInConfiguredAccount = allowPrimary,
        };
        options.AdditionalAccountUris.Add(new Uri("https://archive.invalid"));
        options.AdditionalAccountUris.Add(new Uri("https://127.0.0.1:10000/accountb"));
        BlobClientOptions clientOptions = new()
        {
            Transport = new HttpClientTransport(httpClient),
            TrimBlobNameSlashes = false,
        };
        BlobContainerClient container = new(new Uri("https://account.invalid/new"), credential.Object, clientOptions);
        BlobPayloadStore store = new(options, container, clientOptions);
        string token = $"blob:v2:{blobUri}";

        // Act
        Func<Task> operation = delete
            ? () => store.DeleteAsync(token, CancellationToken.None)
            : () => store.DownloadAsync(token, CancellationToken.None);

        // Assert
        await operation.Should().ThrowAsync<ArgumentException>().WithMessage("*blob name*normalization*");
        blobs.Should().HaveCount(2).And.BeEquivalentTo(expectedBlobs);
        handler.Requests.Should().BeEmpty();
        credential.Invocations.Should().BeEmpty();
    }

    /// <summary>
    /// Permanent blob-name normalization failures retain the tombstone without repeatedly polling it.
    /// </summary>
    [Theory]
    [InlineData("https://archive.invalid/old/folder%2f..%2fblob")]
    [InlineData("https://127.0.0.1:10000/accountb/old/folder%2f.%2fblob")]
    public async Task BlobNameNormalization_PurgeQuarantinesBeforeSendingAsync(string blobUri)
    {
        // Arrange
        using RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        Mock<TokenCredential> credential = CreateCredential();
        LargePayloadStorageOptions options = new(new Uri("https://account.invalid"), credential.Object);
        options.AdditionalAccountUris.Add(new Uri("https://archive.invalid"));
        options.AdditionalAccountUris.Add(new Uri("https://127.0.0.1:10000/accountb"));
        BlobClientOptions clientOptions = new() { Transport = new HttpClientTransport(httpClient) };
        BlobContainerClient container = new(new Uri("https://account.invalid/new"), credential.Object, clientOptions);
        BlobPayloadStore store = new(options, container, clientOptions);
        TestLogger<DeleteExternalBlobActivity> logger = new();
        DeleteExternalBlobActivity activity = new(store, logger);

        // Act
        List<BlobPurgeOutcome> outcomes = await activity.RunAsync(null!, [$"blob:v2:{blobUri}"]);

        // Assert
        outcomes.Should().ContainSingle().Which.Disposition.Should().Be(LargePayloadPurgeDisposition.Quarantined);
        logger.Logs.Should().ContainSingle(l => l.Message.Contains("MalformedToken"));
        handler.Requests.Should().BeEmpty();
        credential.Invocations.Should().BeEmpty();
    }

    /// <summary>
    /// Storage rejection at an authorized additional account does not trigger authentication fallback.
    /// </summary>
    [Fact]
    public async Task AdditionalAccount_StorageDenialDoesNotFallbackAsync()
    {
        // Arrange
        using RecordingHandler handler = new() { DenyAccess = true };
        using HttpClient httpClient = new(handler);
        Mock<TokenCredential> credential = CreateCredential();
        LargePayloadStorageOptions options = new(new Uri("https://account.invalid"), credential.Object)
        {
            ConnectionString = "UseDevelopmentStorage=true",
        };
        options.AdditionalAccountUris.Add(new Uri("https://other.invalid"));
        BlobClientOptions clientOptions = new()
        {
            Transport = new HttpClientTransport(httpClient),
            Retry = { MaxRetries = 0 },
        };
        BlobContainerClient container = new(new Uri("https://account.invalid/new"), credential.Object, clientOptions);
        BlobPayloadStore store = new(options, container, clientOptions);
        string token = "blob:v2:https://other.invalid/old/blob";

        // Act
        Func<Task> download = () => store.DownloadAsync(token, CancellationToken.None);
        Func<Task> delete = () => store.DeleteAsync(token, CancellationToken.None);

        // Assert
        (await download.Should().ThrowAsync<RequestFailedException>()).Which.Status.Should().Be(403);
        (await delete.Should().ThrowAsync<RequestFailedException>()).Which.Status.Should().Be(403);
        handler.Requests.Should().HaveCount(2).And.OnlyContain(
            r => r.Uri.AbsoluteUri == "https://other.invalid/old/blob" && r.AuthorizationScheme == "Bearer");
    }

    /// <summary>
    /// Listing an account does not authorize similar hosts, alternate ports, or other path-style accounts.
    /// </summary>
    [Theory]
    [InlineData("https://other.invalid", "https://unlisted.invalid/old/blob")]
    [InlineData("https://other.invalid", "https://other.invalid.unlisted.invalid/old/blob")]
    [InlineData("https://other.invalid", "https://other.invalid:444/old/blob")]
    [InlineData("https://other.invalid:444", "https://other.invalid/old/blob")]
    [InlineData("https://other.invalid", "http://other.invalid/old/blob")]
    [InlineData("https://127.0.0.1:10000/accountb", "https://127.0.0.1:10000/accountc/old/blob")]
    [InlineData("https://127.0.0.1:10000/accountb", "https://127.0.0.1:10000/Accountb/old/blob")]
    [InlineData("https://127.0.0.1:10000/accountb", "https://127.0.0.1:10000/accountb/%2e%2e/accountc/old/blob")]
    [InlineData("https://127.0.0.1:10000/accountb", "https://127.0.0.1:10000/accountb%2f..%2faccountc/old/blob")]
    [InlineData("https://127.0.0.1:10000/accountb", "https://127.0.0.1:10000/accountb/..%2faccountc%2fold/blob")]
    [InlineData("https://127.0.0.1:10000/accountb", "https://127.0.0.1:10000/accountb/..%252faccountc%252fold/blob")]
    [InlineData("https://other.invalid", "https://account.invalid/old/blob")]
    public async Task AdditionalAccount_UnlistedDestinationRejectsBeforeCredentialAccessAsync(
        string additionalAccount, string blobUri)
    {
        // Arrange
        using RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        Mock<TokenCredential> credential = new(MockBehavior.Strict);
        LargePayloadStorageOptions options = new(new Uri("https://account.invalid"), credential.Object);
        options.AdditionalAccountUris.Add(new Uri(additionalAccount));
        BlobClientOptions clientOptions = new() { Transport = new HttpClientTransport(httpClient) };
        BlobContainerClient container = new(new Uri("https://account.invalid/new"), credential.Object, clientOptions);
        BlobPayloadStore store = new(options, container, clientOptions);
        string token = $"blob:v2:{blobUri}";

        // Act
        Func<Task> download = () => store.DownloadAsync(token, CancellationToken.None);
        Func<Task> delete = () => store.DeleteAsync(token, CancellationToken.None);

        // Assert
        await download.Should().ThrowAsync<PayloadStorageException>();
        await delete.Should().ThrowAsync<PayloadStorageException>();
        handler.Requests.Should().BeEmpty();
        credential.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Unsupported additional-account endpoints fail when the store is constructed.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("relative")]
    [InlineData("other.invalid")]
    [InlineData("http://other.invalid")]
    [InlineData("ftp://other.invalid")]
    [InlineData("https://user@other.invalid")]
    [InlineData("https://other.invalid?sig=token")]
    [InlineData("https://other.invalid#fragment")]
    [InlineData("https://other.invalid/container")]
    [InlineData("https://other.invalid/container/blob")]
    [InlineData("https://127.0.0.1:10000/accountb/container")]
    [InlineData("https://127.0.0.1:10000/")]
    [InlineData("https://127.0.0.1:10000/accountb%2fother")]
    [InlineData("https://127.0.0.1:10000/accountb%252fother")]
    [InlineData("https://127.0.0.1:10000/accountb%5cother")]
    public void AdditionalAccount_InvalidEndpointRejectsConfiguration(string? endpoint)
    {
        // Arrange
        Mock<TokenCredential> credential = new(MockBehavior.Strict);
        LargePayloadStorageOptions options = new(new Uri("https://account.invalid"), credential.Object);
        options.AdditionalAccountUris.Add(endpoint is null ? null! : new Uri(endpoint, UriKind.RelativeOrAbsolute));

        // Act
        Action createStore = () => new BlobPayloadStore(options);

        // Assert
        createStore.Should().Throw<ArgumentException>().WithMessage("*AdditionalAccountUris*");
        credential.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Additional accounts require the effective identity authentication mode, not a credential-only fallback.
    /// </summary>
    [Theory]
    [InlineData("UseDevelopmentStorage=true", false)]
    [InlineData("UseDevelopmentStorage=true", true)]
    [InlineData("BlobEndpoint=https://account.invalid;SharedAccessSignature=sv=2025-01-05&sig=fake", false)]
    public void AdditionalAccount_ConnectionStringAuthenticationRejectsConfiguration(
        string connectionString, bool setCredential)
    {
        // Arrange
        Mock<TokenCredential> credential = new(MockBehavior.Strict);
        LargePayloadStorageOptions options = new(connectionString)
        {
            Credential = setCredential ? credential.Object : null,
        };
        options.AdditionalAccountUris.Add(new Uri("https://other.invalid"));

        // Act
        Action createStore = () => new BlobPayloadStore(options);

        // Assert
        createStore.Should().Throw<ArgumentException>().WithMessage("*requires AccountUri and Credential*");
        credential.VerifyNoOtherCalls();
    }

    /// <summary>
    /// A complete identity configuration retains precedence over a connection string.
    /// </summary>
    [Fact]
    public void AdditionalAccount_CompleteIdentityConfigurationDoesNotParseConnectionString()
    {
        // Arrange
        Mock<TokenCredential> credential = new(MockBehavior.Strict);
        LargePayloadStorageOptions options = new(new Uri("https://account.invalid"), credential.Object)
        {
            ConnectionString = "not-a-connection-string",
        };
        options.AdditionalAccountUris.Add(new Uri("https://other.invalid"));

        // Act
        Action createStore = () => new BlobPayloadStore(options);

        // Assert
        createStore.Should().NotThrow();
        credential.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Broadening the container scope does not replace a configured SAS or use a SAS supplied in a token.
    /// </summary>
    [Fact]
    public async Task AccountScope_SasPermissionsStillApplyAsync()
    {
        // Arrange
        using RecordingHandler handler = new() { DenyAccess = true };
        using HttpClient httpClient = new(handler);
        BlobContainerClient container = new(
            new Uri("https://account.invalid/new?sv=2025-01-05&sr=c&sp=r&sig=configured"),
            new BlobClientOptions { Transport = new HttpClientTransport(httpClient), Retry = { MaxRetries = 0 } });
        BlobPayloadStore store = new(new LargePayloadStorageOptions { AllowAllContainersInConfiguredAccount = true }, container);
        string token = "blob:v2:https://account.invalid/old/blob?sig=from-token&snapshot=old&versionid=old#unused";

        // Act
        Func<Task> download = () => store.DownloadAsync(token, CancellationToken.None);
        Func<Task> delete = () => store.DeleteAsync(token, CancellationToken.None);

        // Assert
        (await download.Should().ThrowAsync<RequestFailedException>()).Which.Status.Should().Be(403);
        (await delete.Should().ThrowAsync<RequestFailedException>()).Which.Status.Should().Be(403);
        handler.Requests.Should().HaveCount(2).And.OnlyContain(r => r.Uri.AbsolutePath == "/old/blob");
        handler.Requests.Should().OnlyContain(r => r.Uri.Query.Contains("sig=configured") && !r.Uri.Query.Contains("from-token"));
        handler.Requests.Should().OnlyContain(r => !r.Uri.Query.Contains("snapshot") && !r.Uri.Query.Contains("versionid"));
        handler.Requests.Should().OnlyContain(r => r.AuthorizationScheme == null);
    }

    /// <summary>
    /// Access scopes are fixed for the lifetime of a store rather than following mutable options.
    /// </summary>
    [Fact]
    public async Task AccessScopes_CaptureConfigurationAtConstructionAsync()
    {
        // Arrange
        using RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        Mock<TokenCredential> credential = new(MockBehavior.Strict);
        LargePayloadStorageOptions options = new(new Uri("https://account.invalid"), credential.Object);
        BlobClientOptions clientOptions = new() { Transport = new HttpClientTransport(httpClient) };
        BlobContainerClient container = new(new Uri("https://account.invalid/new"), credential.Object, clientOptions);
        BlobPayloadStore store = new(options, container, clientOptions);
        options.AllowAllContainersInConfiguredAccount = true;
        options.AdditionalAccountUris.Add(new Uri("https://other.invalid"));

        // Act
        Func<Task> sameAccount = () => store.DownloadAsync("blob:v2:https://account.invalid/old/blob", CancellationToken.None);
        Func<Task> additionalAccount = () => store.DeleteAsync("blob:v2:https://other.invalid/old/blob", CancellationToken.None);

        // Assert
        await sameAccount.Should().ThrowAsync<PayloadStorageException>();
        await additionalAccount.Should().ThrowAsync<PayloadStorageException>();
        handler.Requests.Should().BeEmpty();
        credential.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Equivalent primary endpoints must use the primary account-wide switch instead of the additional list.
    /// </summary>
    [Theory]
    [InlineData("https://account.invalid", "https://ACCOUNT.invalid:443/", false)]
    [InlineData("https://account.invalid", "https://ACCOUNT.invalid:443/", true)]
    [InlineData("https://127.0.0.1:10000/accounta", "https://127.0.0.1:10000/accounta/", false)]
    [InlineData("https://127.0.0.1:10000/accounta", "https://127.0.0.1:10000/accounta/", true)]
    public void AdditionalAccount_PrimaryEndpointRejectsConfiguration(string primary, string additional, bool allowPrimary)
    {
        // Arrange
        Mock<TokenCredential> credential = new(MockBehavior.Strict);
        LargePayloadStorageOptions options = new(new Uri(primary), credential.Object)
        {
            AllowAllContainersInConfiguredAccount = allowPrimary,
        };
        options.AdditionalAccountUris.Add(new Uri(additional));

        // Act
        Action createStore = () => new BlobPayloadStore(options);

        // Assert
        createStore.Should().Throw<ArgumentException>().WithMessage("*AllowAllContainersInConfiguredAccount*");
        credential.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Canonically equivalent additional endpoints share one client.
    /// </summary>
    [Theory]
    [InlineData("https://other.invalid", "https://OTHER.invalid:443/")]
    [InlineData("https://127.0.0.1:10000/accountb", "https://127.0.0.1:10000/accountb/")]
    public void AdditionalAccount_DeduplicatesEquivalentEndpoints(string endpoint, string equivalent)
    {
        // Arrange
        Mock<TokenCredential> credential = new(MockBehavior.Strict);
        LargePayloadStorageOptions options = new(new Uri("https://account.invalid"), credential.Object);
        options.AdditionalAccountUris.Add(new Uri(endpoint));
        options.AdditionalAccountUris.Add(new Uri(equivalent));

        // Act
        BlobServiceClient[] clients = BlobPayloadStore.CreateAdditionalAccountClients(
            options, credential.Object, options.AccountUri!, new BlobClientOptions());

        // Assert
        clients.Should().ContainSingle().Which.Uri.Should().Be(new Uri(endpoint));
        credential.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Reference user information is rejected before identity acquisition or storage requests.
    /// </summary>
    [Theory]
    [InlineData("https://user:password@account.invalid/new/blob")]
    [InlineData("https://user@other.invalid/old/blob")]
    public async Task TokenUserInfo_RejectsBeforeCredentialAccessAsync(string blobUri)
    {
        // Arrange
        using RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        Mock<TokenCredential> credential = new(MockBehavior.Strict);
        LargePayloadStorageOptions options = new(new Uri("https://account.invalid"), credential.Object);
        options.AdditionalAccountUris.Add(new Uri("https://other.invalid"));
        BlobClientOptions clientOptions = new() { Transport = new HttpClientTransport(httpClient) };
        BlobContainerClient container = new(new Uri("https://account.invalid/new"), credential.Object, clientOptions);
        BlobPayloadStore store = new(options, container, clientOptions);

        // Act
        Func<Task> download = () => store.DownloadAsync($"blob:v2:{blobUri}", CancellationToken.None);
        Func<Task> delete = () => store.DeleteAsync($"blob:v2:{blobUri}", CancellationToken.None);

        // Assert
        await download.Should().ThrowAsync<ArgumentException>();
        await delete.Should().ThrowAsync<ArgumentException>();
        handler.Requests.Should().BeEmpty();
        credential.VerifyNoOtherCalls();
    }

    /// <summary>
    /// New references retain escaped paths but never include URL authentication or selectors.
    /// </summary>
    [Theory]
    [InlineData("https://user:password@account.invalid/new/a%20b%252Fc%23%3F?sig=configured#unused", "https://account.invalid/new/a%20b%252Fc%23%3F")]
    [InlineData("http://127.0.0.1:10000/accounta/new/blob?sig=configured#unused", "http://127.0.0.1:10000/accounta/new/blob")]
    public void EncodeToken_EmitsLocationOnly(string blobUri, string expectedUri)
    {
        // Arrange
        Uri location = new(blobUri);

        // Act
        string token = BlobPayloadStore.EncodeToken(location);

        // Assert
        token.Should().Be($"blob:v2:{expectedUri}");
    }

    /// <summary>
    /// Upload requests keep configured SAS authentication without persisting it in their returned token.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Upload_WithSasEmitsLocationOnlyAsync(bool compress)
    {
        // Arrange
        using RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        BlobContainerClient container = new(
            new Uri("https://account.invalid/new?sv=2025-01-05&sig=configured"),
            new BlobClientOptions { Transport = new HttpClientTransport(httpClient) });
        BlobPayloadStore store = new(new LargePayloadStorageOptions
        {
            AllowAllContainersInConfiguredAccount = true,
            CompressionEnabled = compress,
        }, container);

        // Act
        string token = await store.UploadAsync("payload", CancellationToken.None);

        // Assert
        Uri location = new(token.Substring(BlobPayloadStore.TokenPrefixV2.Length));
        location.GetLeftPart(UriPartial.Authority).Should().Be("https://account.invalid");
        location.AbsolutePath.Should().StartWith("/new/");
        location.Query.Should().BeEmpty();
        location.Fragment.Should().BeEmpty();
        location.UserInfo.Should().BeEmpty();
        handler.Requests.Should().NotBeEmpty().And.OnlyContain(
            r => r.Method == "PUT" && r.Uri.Host == "account.invalid"
                && r.Uri.AbsolutePath.StartsWith("/new") && r.Uri.Query.Contains("sig=configured"));
    }

    /// <summary>
    /// The two independent switches apply consistently to reads, deletion, legacy tokens, and uploads.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task AccessScopes_ApplyFullMatrixAsync(bool allowPrimary, bool addAccount)
    {
        // Arrange
        using RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        Mock<TokenCredential> credential = CreateCredential();
        LargePayloadStorageOptions options = new(new Uri("https://account.invalid"), credential.Object)
        {
            AllowAllContainersInConfiguredAccount = allowPrimary,
            CompressionEnabled = false,
        };
        if (addAccount)
        {
            options.AdditionalAccountUris.Add(new Uri("https://other.invalid"));
        }

        BlobClientOptions clientOptions = new() { Transport = new HttpClientTransport(httpClient) };
        BlobContainerClient container = new(new Uri("https://account.invalid/new"), credential.Object, clientOptions);
        BlobPayloadStore store = new(options, container, clientOptions);
        (string Token, bool Allowed)[] references =
        [
            ("blob:v2:https://account.invalid/new/blob", true),
            ("blob:v1:new:blob", true),
            ("blob:v2:https://account.invalid/old/blob", allowPrimary),
            ("blob:v1:old:blob", allowPrimary),
            ("blob:v2:https://other.invalid/old/blob", addAccount),
            ("blob:v2:https://unlisted.invalid/new/blob", false),
        ];

        // Act and Assert
        foreach ((string token, bool allowed) in references)
        {
            int requests = handler.Requests.Count;
            int credentialCalls = credential.Invocations.Count;
            if (allowed)
            {
                (await store.DownloadAsync(token, CancellationToken.None)).Should().Be("payload");
                (await store.DeleteAsync(token, CancellationToken.None)).Should().Be(PayloadDeleteOutcome.Deleted);
            }
            else
            {
                Func<Task> download = () => store.DownloadAsync(token, CancellationToken.None);
                Func<Task> delete = () => store.DeleteAsync(token, CancellationToken.None);
                await download.Should().ThrowAsync<Exception>();
                await delete.Should().ThrowAsync<Exception>();
                handler.Requests.Should().HaveCount(requests);
                credential.Invocations.Should().HaveCount(credentialCalls);
            }
        }

        string uploaded = await store.UploadAsync("payload", CancellationToken.None);
        uploaded.Should().StartWith("blob:v2:https://account.invalid/new/");
        handler.Requests.Where(r => r.Method == "PUT").Should().OnlyContain(
            r => r.Uri.Host == "account.invalid" && r.Uri.AbsolutePath.StartsWith("/new"));
    }

    /// <summary>
    /// Additional-account retries do not use a primary account's secondary endpoint.
    /// </summary>
    [Fact]
    public async Task AdditionalAccount_RetryRemainsAtAdditionalEndpointAsync()
    {
        // Arrange
        using RecordingHandler handler = new() { TransientFailures = 1 };
        using HttpClient httpClient = new(handler);
        Mock<TokenCredential> credential = CreateCredential();
        LargePayloadStorageOptions options = new(new Uri("https://account.invalid"), credential.Object);
        options.AdditionalAccountUris.Add(new Uri("https://other.invalid"));
        BlobClientOptions clientOptions = new()
        {
            Transport = new HttpClientTransport(httpClient),
            GeoRedundantSecondaryUri = new Uri("https://primary-secondary.invalid"),
            Retry = { MaxRetries = 1, Delay = TimeSpan.Zero, MaxDelay = TimeSpan.Zero },
        };
        BlobContainerClient container = new(new Uri("https://account.invalid/new"), credential.Object, clientOptions);
        BlobPayloadStore store = new(options, container, clientOptions);

        // Act
        string payload = await store.DownloadAsync("blob:v2:https://other.invalid/old/blob", CancellationToken.None);

        // Assert
        payload.Should().Be("payload");
        handler.Requests.Should().HaveCount(2).And.OnlyContain(
            r => r.Uri.AbsoluteUri == "https://other.invalid/old/blob" && r.AuthorizationScheme == "Bearer");
    }

    /// <summary>
    /// Mutating endpoint and credential options cannot change an existing store's selected clients.
    /// </summary>
    [Fact]
    public async Task AccessScopes_CaptureEndpointAndIdentityAsync()
    {
        // Arrange
        using RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        Mock<TokenCredential> originalCredential = CreateCredential();
        Mock<TokenCredential> replacementCredential = new(MockBehavior.Strict);
        LargePayloadStorageOptions options = new(new Uri("https://account.invalid"), originalCredential.Object);
        options.AdditionalAccountUris.Add(new Uri("https://other.invalid"));
        BlobClientOptions clientOptions = new() { Transport = new HttpClientTransport(httpClient) };
        BlobContainerClient container = new(new Uri("https://account.invalid/new"), originalCredential.Object, clientOptions);
        BlobPayloadStore store = new(options, container, clientOptions);
        options.AccountUri = new Uri("https://replacement.invalid");
        options.ContainerName = "old";
        options.Credential = replacementCredential.Object;
        options.AdditionalAccountUris.Clear();
        options.AdditionalAccountUris.Add(new Uri("https://replacement.invalid"));

        // Act
        string primary = await store.DownloadAsync("blob:v2:https://account.invalid/new/blob", CancellationToken.None);
        string additional = await store.DownloadAsync("blob:v2:https://other.invalid/old/blob", CancellationToken.None);
        Func<Task> replacement = () => store.DownloadAsync("blob:v2:https://replacement.invalid/old/blob", CancellationToken.None);

        // Assert
        primary.Should().Be("payload");
        additional.Should().Be("payload");
        await replacement.Should().ThrowAsync<PayloadStorageException>();
        handler.Requests.Should().HaveCount(2);
        replacementCredential.VerifyNoOtherCalls();
        originalCredential.Verify(
            c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    /// <summary>
    /// The public store's default transport surfaces redirects without following another account.
    /// </summary>
    [Theory]
    [InlineData("blob")]
    [InlineData("/blob")]
    [InlineData("blob/")]
    public async Task Download_RedirectDoesNotChangeEndpointAsync(string blobName)
    {
        // Arrange
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        string? requestLine = null;
        Task server = ServeRedirectAsync();
        BlobPayloadStore store = new(new LargePayloadStorageOptions(
            $"BlobEndpoint=http://127.0.0.1:{port}/accounta;AccountName=accounta;AccountKey={Convert.ToBase64String(new byte[64])}")
        {
            ContainerName = "new",
        });

        // Act
        Func<Task> download = () => store.DownloadAsync(
            $"blob:v2:http://127.0.0.1:{port}/accounta/new/{blobName}", timeout.Token);

        // Assert
        (await download.Should().ThrowAsync<RequestFailedException>()).Which.Status.Should().Be(307);
        await server;
        requestLine.Should().Be($"GET /accounta/new/{blobName} HTTP/1.1");

        async Task ServeRedirectAsync()
        {
            using TcpClient connection = await listener.AcceptTcpClientAsync(timeout.Token);
            using NetworkStream stream = connection.GetStream();
            using StreamReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
            requestLine = await reader.ReadLineAsync(timeout.Token);
            while (await reader.ReadLineAsync(timeout.Token) is string line && line.Length != 0)
            {
                // Consume and ignore the remaining HTTP headers.
            }

            byte[] response = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 307 Temporary Redirect\r\nLocation: http://127.0.0.1:{port}/accountb/new/blob\r\n" +
                "Content-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response, timeout.Token);
        }
    }

    /// <summary>
    /// Listing another account does not implicitly grant account-wide access in the configured account.
    /// </summary>
    [Fact]
    public async Task AdditionalAccount_PrimaryContainerStillWorksAsync()
    {
        // Arrange
        using RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        Mock<TokenCredential> credential = CreateCredential();
        LargePayloadStorageOptions options = new(new Uri("https://account.invalid"), credential.Object);
        options.AdditionalAccountUris.Add(new Uri("https://other.invalid"));
        BlobClientOptions clientOptions = new() { Transport = new HttpClientTransport(httpClient) };
        BlobContainerClient container = new(new Uri("https://account.invalid/new"), credential.Object, clientOptions);
        BlobPayloadStore store = new(options, container, clientOptions);

        // Act
        string payload = await store.DownloadAsync("blob:v2:https://account.invalid/new/blob", CancellationToken.None);
        PayloadDeleteOutcome outcome = await store.DeleteAsync("blob:v1:new:blob", CancellationToken.None);

        // Assert
        payload.Should().Be("payload");
        outcome.Should().Be(PayloadDeleteOutcome.Deleted);
        handler.Requests.Should().HaveCount(3).And.OnlyContain(
            r => r.Uri.AbsoluteUri == "https://account.invalid/new/blob" && r.AuthorizationScheme == "Bearer");
    }

    /// <summary>
    /// Explicit additional-account scopes preserve retry behavior for references that remain disallowed.
    /// </summary>
    [Theory]
    [InlineData("other", LargePayloadPurgeDisposition.Deleted)]
    [InlineData("unlisted", LargePayloadPurgeDisposition.Retry)]
    public async Task Purge_UsesAdditionalAccountScopeAsync(string account, LargePayloadPurgeDisposition disposition)
    {
        // Arrange
        using RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        Mock<TokenCredential> credential = CreateCredential();
        LargePayloadStorageOptions options = new(new Uri("https://account.invalid"), credential.Object);
        options.AdditionalAccountUris.Add(new Uri("https://other.invalid"));
        BlobClientOptions clientOptions = new() { Transport = new HttpClientTransport(httpClient) };
        BlobContainerClient container = new(new Uri("https://account.invalid/new"), credential.Object, clientOptions);
        BlobPayloadStore store = new(options, container, clientOptions);
        TestLogger<DeleteExternalBlobActivity> logger = new();
        DeleteExternalBlobActivity activity = new(store, logger);

        // Act
        List<BlobPurgeOutcome> outcomes = await activity.RunAsync(
            null!, [$"blob:v2:https://{account}.invalid/old/blob"]);

        // Assert
        outcomes.Should().ContainSingle().Which.Disposition.Should().Be(disposition);
        if (disposition == LargePayloadPurgeDisposition.Retry)
        {
            handler.Requests.Should().BeEmpty();
            credential.VerifyNoOtherCalls();
        }
        else
        {
            handler.Requests.Select(r => r.Method).Should().Equal("HEAD", "DELETE");
            handler.Requests[1].IfMatch.Should().Be("\"owned-etag\"");
        }
    }

    static Mock<TokenCredential> CreateCredential()
    {
        Mock<TokenCredential> credential = new(MockBehavior.Strict);
        AccessToken token = new("offline-test-token", DateTimeOffset.UtcNow.AddHours(1));
        credential.Setup(c => c.GetToken(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>())).Returns(token);
        credential.Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(token);
        return credential;
    }

    sealed record RecordedRequest(string Method, Uri Uri, string? AuthorizationScheme, string? IfMatch);

    sealed class RecordingHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = new();

        public bool DenyAccess { get; init; }

        public int TransientFailures { get; init; }

        public Dictionary<string, string>? BlobPayloads { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(this.RecordAndRespond(request));

        protected override HttpResponseMessage Send(
            HttpRequestMessage request, CancellationToken cancellationToken) => this.RecordAndRespond(request);

        HttpResponseMessage RecordAndRespond(HttpRequestMessage request)
        {
            this.Requests.Add(new(
                request.Method.Method, request.RequestUri!, request.Headers.Authorization?.Scheme,
                request.Headers.IfMatch.FirstOrDefault()?.ToString()));
            if (this.Requests.Count <= this.TransientFailures)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("<Error><Code>ServerBusy</Code></Error>", Encoding.UTF8, "application/xml"),
                };
            }

            string payload = "payload";
            if (this.BlobPayloads is not null)
            {
                if (!this.BlobPayloads.TryGetValue(request.RequestUri!.GetLeftPart(UriPartial.Path), out string? storedPayload))
                {
                    HttpResponseMessage missing = new(HttpStatusCode.NotFound)
                    {
                        Content = new StringContent("<Error><Code>BlobNotFound</Code></Error>", Encoding.UTF8, "application/xml"),
                    };
                    missing.Headers.TryAddWithoutValidation("x-ms-error-code", "BlobNotFound");
                    return missing;
                }

                payload = storedPayload;
                if (request.Method == HttpMethod.Delete)
                {
                    this.BlobPayloads.Remove(request.RequestUri!.GetLeftPart(UriPartial.Path));
                }
            }

            HttpResponseMessage response = new(this.DenyAccess
                ? HttpStatusCode.Forbidden
                : request.Method == HttpMethod.Delete ? HttpStatusCode.Accepted
                : request.Method == HttpMethod.Put ? HttpStatusCode.Created : HttpStatusCode.OK);
            response.Content = new StringContent(
                this.DenyAccess
                    ? "<Error><Code>AuthorizationPermissionMismatch</Code><Message>Denied by test service.</Message></Error>"
                    : payload,
                Encoding.UTF8,
                this.DenyAccess ? "application/xml" : "text/plain");
            response.Headers.TryAddWithoutValidation("x-ms-request-id", "offline");
            if (this.DenyAccess)
            {
                response.Headers.TryAddWithoutValidation("x-ms-error-code", "AuthorizationPermissionMismatch");
            }
            else
            {
                response.Headers.ETag = new(this.BlobPayloads is null ? "\"owned-etag\"" : $"\"{payload}-etag\"");
                response.Headers.TryAddWithoutValidation("x-ms-meta-managed_by", "dts");
                response.Headers.TryAddWithoutValidation("x-ms-blob-type", "BlockBlob");
                response.Content.Headers.LastModified = DateTimeOffset.UnixEpoch;
            }

            return response;
        }
    }
}
