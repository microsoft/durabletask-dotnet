// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Client;
using static Microsoft.DurableTask.Protobuf.TaskHubSidecarService;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests.AutoPurge;

/// <summary>
/// The two purge activities are the real runtime path to the backend - they call the generated sidecar client
/// directly. During a mixed rollout an older backend returns gRPC <c>Unimplemented</c> for the new purge RPCs;
/// each activity must translate that into a <see cref="NotImplementedException"/> so the orchestrator can
/// disable the job instead of retrying an operation that can never succeed.
/// </summary>
public class PurgeActivityUnimplementedTests
{
    [Fact]
    public async Task GetLargePayloadTombstones_WhenBackendUnimplemented_ThrowsNotImplemented()
    {
        // Arrange - the backend rejects the fetch RPC because it does not implement it.
        TaskHubSidecarServiceClient client = new(
            new ThrowingCallInvoker(new RpcException(new Status(StatusCode.Unimplemented, "unknown method"))));
        GetLargePayloadTombstonesActivity activity = new(client, new TestLogger<GetLargePayloadTombstonesActivity>());

        // Act
        Func<Task> act = () => activity.RunAsync(null!, 100);

        // Assert - a bare RpcException would let the retry policy spin forever; NotImplementedException is the
        // signal the orchestrator matches on to stop.
        await act.Should().ThrowAsync<NotImplementedException>();
    }

    [Fact]
    public async Task ReportLargePayloadPurgeResults_WhenBackendUnimplemented_ThrowsNotImplemented()
    {
        // Arrange - a non-empty result list is required to get past the activity's empty-input short-circuit and
        // actually reach the report RPC, which the backend does not implement.
        TaskHubSidecarServiceClient client = new(
            new ThrowingCallInvoker(new RpcException(new Status(StatusCode.Unimplemented, "unknown method"))));
        ReportLargePayloadPurgeResultsActivity activity =
            new(client, new TestLogger<ReportLargePayloadPurgeResultsActivity>());
        List<LargePayloadPurgeResult> results = new()
        {
            new LargePayloadPurgeResult(1, 2, 3, 4, LargePayloadPurgeDisposition.Deleted),
        };

        // Act
        Func<Task> act = () => activity.RunAsync(null!, results);

        // Assert
        await act.Should().ThrowAsync<NotImplementedException>();
    }

    /// <summary>
    /// A minimal <see cref="CallInvoker"/> whose unary calls fault with a configured <see cref="RpcException"/>.
    /// Building a real <see cref="TaskHubSidecarServiceClient"/> over it exercises the activity's own catch
    /// clause exactly as a live channel would, without mocking the generated client. The faulted
    /// <see cref="AsyncUnaryCall{TResponse}"/> surfaces the exception on await, matching real gRPC.
    /// </summary>
    sealed class ThrowingCallInvoker(RpcException error) : CallInvoker
    {
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
            => new(
                Task.FromException<TResponse>(error),
                Task.FromResult(new Metadata()),
                () => error.Status,
                () => new Metadata(),
                () => { });

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
            => throw error;

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
            => throw new NotSupportedException();

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options)
            => throw new NotSupportedException();

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options)
            => throw new NotSupportedException();
    }
}
