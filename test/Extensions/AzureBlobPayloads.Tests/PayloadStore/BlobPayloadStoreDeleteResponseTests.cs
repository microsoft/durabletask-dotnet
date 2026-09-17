// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Azure;
using Azure.Core.Pipeline;
using Azure.Storage;
using Azure.Storage.Blobs;
using Grpc.Net.Client;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using LP = Microsoft.DurableTask.Protobuf.LargePayloads;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests;

public class BlobPayloadStoreDeleteResponseTests
{
    const string Token = "blob:v2:https://account.invalid/payloads/blob";

    [Theory]
    [InlineData("HEAD", 404, "BlobNotFound", PayloadDeleteOutcome.AlreadyAbsent)]
    [InlineData("HEAD", 404, "ContainerNotFound", PayloadDeleteOutcome.AlreadyAbsent)]
    [InlineData("HEAD", 404, "UnknownNotFound", null)]
    [InlineData("HEAD", 404, null, null)]
    [InlineData("HEAD", 404, "", null)]
    [InlineData("HEAD", 403, "AuthorizationFailure", null)]
    [InlineData("DELETE", 202, null, PayloadDeleteOutcome.Deleted)]
    [InlineData("DELETE", 404, "BlobNotFound", PayloadDeleteOutcome.AlreadyAbsent)]
    [InlineData("DELETE", 404, "ContainerNotFound", PayloadDeleteOutcome.AlreadyAbsent)]
    [InlineData("DELETE", 404, "UnknownNotFound", null)]
    [InlineData("DELETE", 404, null, null)]
    [InlineData("DELETE", 404, "", null)]
    [InlineData("DELETE", 403, "AuthorizationFailure", null)]
    [InlineData("HEAD", 200, null, PayloadDeleteOutcome.NotStoreOwned, false)]
    public async Task DeleteResponse_ReportsOnlyConfirmedSuccessAsync(
        string responseMethod, int status, string? errorCode, PayloadDeleteOutcome? expectedOutcome, bool owned = true)
    {
        // Arrange
        using ResponseHandler handler = new(responseMethod, status, errorCode, owned);
        using HttpClient http = new(handler, disposeHandler: false);
        BlobContainerClient container = new(
            new Uri("https://account.invalid/payloads"),
            new StorageSharedKeyCredential("account", Convert.ToBase64String(new byte[64])),
            new BlobClientOptions { Transport = new HttpClientTransport(http), Retry = { MaxRetries = 0 } });
        BlobPayloadStore store = new(new LargePayloadStorageOptions(), container);
        TestLogger<DeleteExternalBlobActivity> logger = new();
        using GrpcChannel reportChannel = GrpcChannel.ForAddress("http://report.invalid", new() { HttpHandler = handler });
        ReportLargePayloadPurgeResultsActivity report = new(
            new LP.LargePayloadPurge.LargePayloadPurgeClient(reportChannel),
            NullLogger<ReportLargePayloadPurgeResultsActivity>.Instance);

        // Act
        PayloadDeleteOutcome? storeOutcome = null;
        Exception? storeFailure = await Record.ExceptionAsync(async () =>
        {
            storeOutcome = await store.DeleteAsync(Token, CancellationToken.None);
        });
        BlobPurgeOutcome outcome = Assert.Single(await new DeleteExternalBlobActivity(store, logger).RunAsync(null!, [Token]));
        await report.RunAsync(null!, [new LargePayloadPurgeResult("opaque-tombstone", outcome.Disposition)]);

        // Assert
        LargePayloadPurgeDisposition expectedDisposition = expectedOutcome.HasValue
            ? LargePayloadPurgeDisposition.Deleted : LargePayloadPurgeDisposition.Retry;
        Assert.Equal(expectedDisposition, outcome.Disposition);
        if (expectedOutcome.HasValue)
        {
            Assert.Null(storeFailure);
            Assert.Equal(expectedOutcome, storeOutcome);
        }
        else
        {
            RequestFailedException failure = Assert.IsType<RequestFailedException>(storeFailure);
            Assert.Equal(status, failure.Status);
            Assert.Equal(errorCode ?? string.Empty, failure.ErrorCode ?? string.Empty);
        }

        LP.LargePayloadPurgeResult wire = Assert.Single(Assert.IsType<LP.ReportLargePayloadPurgeResultsRequest>(handler.Report).Results);
        Assert.Equal("opaque-tombstone", wire.TombstoneToken);
        Assert.Equal((LP.LargePayloadPurgeDisposition)expectedDisposition, wire.Disposition);
        Assert.Equal(
            responseMethod == "HEAD" ? ["HEAD", "HEAD"] : new[] { "HEAD", "DELETE", "HEAD", "DELETE" },
            handler.Requests.Select(request => request.Method));
        Assert.All(handler.Requests.Where(request => request.Method == "DELETE"),
            request => Assert.Equal("\"owned-etag\"", request.IfMatch));
        if (!expectedOutcome.HasValue)
        {
            Assert.Contains(logger.Logs, entry => entry.Message.Contains(
                status == 403 ? "StorageAuthorizationFailed" : "TransientStorageFailure", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Logs, entry => entry.Message.Contains(Token, StringComparison.Ordinal));
        }
    }

    sealed class ResponseHandler(string responseMethod, int status, string? errorCode, bool owned) : HttpMessageHandler
    {
        public List<(string Method, string? IfMatch)> Requests { get; } = [];

        public LP.ReportLargePayloadPurgeResultsRequest? Report { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "report.invalid")
            {
                Assert.EndsWith("/ReportLargePayloadPurgeResults", request.RequestUri.AbsolutePath);
                byte[] frame = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                Assert.Equal(0, frame[0]);
                Assert.Equal(frame.Length - 5, BinaryPrimitives.ReadInt32BigEndian(frame.AsSpan(1, 4)));
                this.Report = LP.ReportLargePayloadPurgeResultsRequest.Parser.ParseFrom(frame.AsSpan(5).ToArray());
                HttpResponseMessage reportResponse = new(HttpStatusCode.OK)
                {
                    Version = new(2, 0),
                    Content = new ByteArrayContent(new byte[5]),
                };
                reportResponse.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
                reportResponse.TrailingHeaders.TryAddWithoutValidation("grpc-status", "0");
                return reportResponse;
            }

            Assert.Equal("account.invalid", request.RequestUri.Host);
            Assert.Contains(request.Method.Method, new[] { "HEAD", "DELETE" });
            this.Requests.Add((request.Method.Method, request.Headers.IfMatch.FirstOrDefault()?.ToString()));
            int responseStatus = request.Method.Method == responseMethod ? status : 200;
            bool failed = responseStatus >= 400;
            string body = failed
                ? "<Error>" + (errorCode is null ? "" : $"<Code>{errorCode}</Code>") + "<Message>Test failure.</Message></Error>"
                : "";
            HttpResponseMessage response = new((HttpStatusCode)responseStatus)
            {
                Content = new StringContent(body, Encoding.UTF8, failed ? "application/xml" : "text/plain"),
            };
            response.Headers.TryAddWithoutValidation("x-ms-request-id", "offline");
            if (failed && errorCode is not null)
            {
                response.Headers.TryAddWithoutValidation("x-ms-error-code", errorCode);
            }
            else if (!failed)
            {
                response.Headers.ETag = new("\"owned-etag\"");
                response.Headers.TryAddWithoutValidation("x-ms-blob-type", "BlockBlob");
                response.Content.Headers.LastModified = DateTimeOffset.UnixEpoch;
                if (owned)
                {
                    response.Headers.TryAddWithoutValidation("x-ms-meta-managed_by", "dts");
                }
            }

            return response;
        }
    }
}
