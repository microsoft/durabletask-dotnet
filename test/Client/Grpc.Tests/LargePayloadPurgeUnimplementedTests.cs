// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Client.Grpc.Tests;

/// <summary>
/// Public-API parity for the mixed-rollout mapping: the two large-payload purge overrides on
/// <see cref="GrpcDurableTaskClient"/> must translate a gRPC <c>Unimplemented</c> from an older backend into a
/// <see cref="NotImplementedException"/>, matching the precedent already used elsewhere in the client for
/// unsupported RPCs.
/// </summary>
public class LargePayloadPurgeUnimplementedTests
{
    readonly Mock<ILogger> loggerMock = new();

    [Fact]
    public async Task GetLargePayloadTombstonesAsync_WhenBackendUnimplemented_ThrowsNotImplemented()
    {
        // Arrange - the backend does not implement the fetch RPC.
        GrpcDurableTaskClient client = this.CreateThrowingClient(
            new RpcException(new Status(StatusCode.Unimplemented, "unknown method")));

        // Act
        Func<Task> act = () => client.GetLargePayloadTombstonesAsync(100);

        // Assert
        await act.Should().ThrowAsync<NotImplementedException>();
    }

    [Fact]
    public async Task ReportLargePayloadPurgeResultsAsync_WhenBackendUnimplemented_ThrowsNotImplemented()
    {
        // Arrange - a non-empty result list is required to reach the RPC past the empty-request short-circuit.
        GrpcDurableTaskClient client = this.CreateThrowingClient(
            new RpcException(new Status(StatusCode.Unimplemented, "unknown method")));
        LargePayloadPurgeResult[] results =
        {
            new(1, 2, 3, 4, LargePayloadPurgeDisposition.Deleted),
        };

        // Act
        Func<Task> act = () => client.ReportLargePayloadPurgeResultsAsync(results);

        // Assert
        await act.Should().ThrowAsync<NotImplementedException>();
    }

    GrpcDurableTaskClient CreateThrowingClient(RpcException error)
    {
        GrpcDurableTaskClientOptions options = new()
        {
            CallInvoker = new ThrowingCallInvoker(error),
        };

        return new GrpcDurableTaskClient("test", options, this.loggerMock.Object);
    }

    /// <summary>
    /// A minimal <see cref="CallInvoker"/> whose unary calls fault with a configured <see cref="RpcException"/>,
    /// so the client's real RPC path runs and hits its own catch clause exactly as a live channel would.
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
