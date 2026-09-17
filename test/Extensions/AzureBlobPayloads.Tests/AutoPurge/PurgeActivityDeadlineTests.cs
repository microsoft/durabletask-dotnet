// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Client;
using static Microsoft.DurableTask.Protobuf.LargePayloads.LargePayloadPurge;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests.AutoPurge;

/// <summary>
/// Verifies deadline expiry through the pinned gRPC transport without any network requests.
/// </summary>
public class PurgeActivityDeadlineTests
{
    /// <summary>
    /// A blocked fetch is canceled by gRPC and propagates DeadlineExceeded for activity retry.
    /// </summary>
    [Fact]
    public async Task GetLargePayloadTombstones_WhenDeadlineExpires_CancelsRequestAsync()
    {
        // Arrange
        using BlockingHttpMessageHandler handler = new();
        using GrpcChannel channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = handler });
        LargePayloadPurgeClient client = new(channel);
        GetLargePayloadTombstonesActivity activity = new(client, new TestLogger<GetLargePayloadTombstonesActivity>())
        {
            RpcTimeout = TimeSpan.FromMilliseconds(200),
        };

        // Act
        Func<Task> act = () => activity.RunAsync(null!, 100).WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        await act.Should().ThrowAsync<RpcException>().Where(e => e.StatusCode == StatusCode.DeadlineExceeded);
        await handler.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// A blocked report is canceled by gRPC and propagates DeadlineExceeded for activity retry.
    /// </summary>
    [Fact]
    public async Task ReportLargePayloadPurgeResults_WhenDeadlineExpires_CancelsRequestAsync()
    {
        // Arrange
        using BlockingHttpMessageHandler handler = new();
        using GrpcChannel channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = handler });
        LargePayloadPurgeClient client = new(channel);
        ReportLargePayloadPurgeResultsActivity activity = new(client, new TestLogger<ReportLargePayloadPurgeResultsActivity>())
        {
            RpcTimeout = TimeSpan.FromMilliseconds(200),
        };
        List<LargePayloadPurgeResult> results = new()
        {
            new LargePayloadPurgeResult("tombstone-token-1", LargePayloadPurgeDisposition.Deleted),
        };

        // Act
        Func<Task> act = () => activity.RunAsync(null!, results).WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        await act.Should().ThrowAsync<RpcException>().Where(e => e.StatusCode == StatusCode.DeadlineExceeded);
        await handler.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    sealed class BlockingHttpMessageHandler : HttpMessageHandler
    {
        public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using CancellationTokenRegistration registration = cancellationToken.Register(() => this.CancellationObserved.TrySetResult());
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The handler must remain blocked until the request is canceled.");
        }
    }
}
