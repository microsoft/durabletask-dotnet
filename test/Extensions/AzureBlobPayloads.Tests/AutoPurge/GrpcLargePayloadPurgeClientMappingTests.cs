// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Client;
using static Microsoft.DurableTask.Protobuf.LargePayloads.LargePayloadPurge;
using Proto = Microsoft.DurableTask.Protobuf.LargePayloads;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests.AutoPurge;

/// <summary>
/// Exercises the real <see cref="GrpcLargePayloadPurgeClient"/> adapter against a transport that returns
/// genuinely populated protobuf responses, proving the field-by-field mapping between the generated protobuf
/// messages and the SDK's canonical models. The other transport tests in this project only ever see empty
/// responses, which cannot catch a swapped field, a swapped row, or a disposition mapped to a constant value.
/// </summary>
public class GrpcLargePayloadPurgeClientMappingTests
{
    // Deliberately opaque-token-like values with whitespace, quotes, and base64-style '+'/'=' characters, so a
    // naive trim/escape/re-encode bug in the adapter would also be caught, not only a swap or drop.
    const string FirstTombstoneToken = " tomb\"stone\"+one== ";
    const string FirstPayloadToken = "blob:v2:https://acct.blob.core.windows.net/c/\"one\"+a==";
    const string SecondTombstoneToken = "tomb/stone two==";
    const string SecondPayloadToken = " blob:v2:https://acct.blob.core.windows.net/c/two+b== ";

    [Fact]
    public async Task GetLargePayloadTombstonesAsync_MapsEveryFieldInOrderWithoutSwappingOrDroppingAsync()
    {
        // Arrange - a transport that actually returns two distinct, order-sensitive tombstones.
        RespondingCallInvoker invoker = new();
        invoker.TombstonesResponse.Tombstones.Add(new Proto.LargePayloadTombstone
        {
            TombstoneToken = FirstTombstoneToken,
            PayloadToken = FirstPayloadToken,
        });
        invoker.TombstonesResponse.Tombstones.Add(new Proto.LargePayloadTombstone
        {
            TombstoneToken = SecondTombstoneToken,
            PayloadToken = SecondPayloadToken,
        });
        LargePayloadPurgeClient client = new(invoker);
        GrpcLargePayloadPurgeClient adapter = new(client);

        // Act - the real adapter, not a mock of ILargePayloadPurgeClient.
        List<LargePayloadTombstone> tombstones =
            await adapter.GetLargePayloadTombstonesAsync(100, DateTime.UtcNow.AddSeconds(30));

        // Assert - exact count, order, and per-field values. A swap of TombstoneToken<->PayloadToken within one
        // row, a swap of the two rows, or a dropped row would all fail this.
        tombstones.Should().HaveCount(2);
        tombstones[0].TombstoneToken.Should().Be(FirstTombstoneToken);
        tombstones[0].PayloadToken.Should().Be(FirstPayloadToken);
        tombstones[1].TombstoneToken.Should().Be(SecondTombstoneToken);
        tombstones[1].PayloadToken.Should().Be(SecondPayloadToken);
    }

    [Fact]
    public async Task ReportLargePayloadPurgeResultsAsync_SendsEveryResultInOrderWithoutSwappingOrDroppingAsync()
    {
        // Arrange - two distinct results with different dispositions, so a disposition mapped to a constant
        // value would also be caught, not only a dropped or swapped token.
        RespondingCallInvoker invoker = new();
        LargePayloadPurgeClient client = new(invoker);
        GrpcLargePayloadPurgeClient adapter = new(client);
        List<LargePayloadPurgeResult> results =
        [
            new LargePayloadPurgeResult(FirstTombstoneToken, LargePayloadPurgeDisposition.Deleted),
            new LargePayloadPurgeResult(SecondTombstoneToken, LargePayloadPurgeDisposition.Quarantined),
        ];

        // Act - the real adapter builds and sends the outgoing protobuf request.
        await adapter.ReportLargePayloadPurgeResultsAsync(results, DateTime.UtcNow.AddSeconds(30));

        // Assert - the transport captured exactly what the adapter actually sent.
        invoker.ReportRequest.Should().NotBeNull();
        invoker.ReportRequest!.Results.Should().HaveCount(2);
        invoker.ReportRequest.Results[0].TombstoneToken.Should().Be(FirstTombstoneToken);
        invoker.ReportRequest.Results[0].Disposition.Should().Be(Proto.LargePayloadPurgeDisposition.Deleted);
        invoker.ReportRequest.Results[1].TombstoneToken.Should().Be(SecondTombstoneToken);
        invoker.ReportRequest.Results[1].Disposition.Should().Be(Proto.LargePayloadPurgeDisposition.Quarantined);
    }

    /// <summary>
    /// A minimal <see cref="CallInvoker"/> that answers the two purge RPCs with real, populated protobuf
    /// responses and captures the actual outgoing request, rather than mocking the generated client or the
    /// shared transport interface.
    /// </summary>
    sealed class RespondingCallInvoker : CallInvoker
    {
        public Proto.GetLargePayloadTombstonesResponse TombstonesResponse { get; } = new();

        public Proto.ReportLargePayloadPurgeResultsRequest? ReportRequest { get; private set; }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            object response = request switch
            {
                Proto.GetLargePayloadTombstonesRequest => this.TombstonesResponse,
                Proto.ReportLargePayloadPurgeResultsRequest report => this.Respond(report),
                _ => throw new NotSupportedException(method.Name),
            };
            return new(
                Task.FromResult((TResponse)response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { });
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
            => throw new NotSupportedException();

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
            => throw new NotSupportedException();

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options)
            => throw new NotSupportedException();

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options)
            => throw new NotSupportedException();

        Proto.ReportLargePayloadPurgeResultsResponse Respond(Proto.ReportLargePayloadPurgeResultsRequest request)
        {
            this.ReportRequest = request;
            return new Proto.ReportLargePayloadPurgeResultsResponse();
        }
    }
}
