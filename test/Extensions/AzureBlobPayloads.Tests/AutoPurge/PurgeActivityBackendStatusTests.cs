// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using static Microsoft.DurableTask.Protobuf.LargePayloads.LargePayloadPurge;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests.AutoPurge;

/// <summary>
/// The two purge activities are the real runtime path to the backend - they call the generated purge service client
/// directly, so the status the backend answers with is theirs to interpret. An older backend answers
/// <c>Unimplemented</c> for the new purge RPCs and must become a <see cref="NotImplementedException"/> so the
/// orchestrator can disable the job; a current backend answers <c>FailedPrecondition</c> when the task hub is not
/// in a state that permits a fetch, which is expected and must degrade to an empty batch.
/// </summary>
public class PurgeActivityBackendStatusTests
{
    [Fact]
    public async Task GetLargePayloadTombstones_WhenBackendUnimplemented_ThrowsNotImplemented()
    {
        // Arrange - the backend rejects the fetch RPC because it does not implement it.
        LargePayloadPurgeClient client = new(
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
        LargePayloadPurgeClient client = new(
            new ThrowingCallInvoker(new RpcException(new Status(StatusCode.Unimplemented, "unknown method"))));
        ReportLargePayloadPurgeResultsActivity activity =
            new(client, new TestLogger<ReportLargePayloadPurgeResultsActivity>());
        List<LargePayloadPurgeResult> results = new()
        {
            new LargePayloadPurgeResult("tombstone-token-1", LargePayloadPurgeDisposition.Deleted),
        };

        // Act
        Func<Task> act = () => activity.RunAsync(null!, results);

        // Assert
        await act.Should().ThrowAsync<NotImplementedException>();
    }

    [Fact]
    public async Task GetLargePayloadTombstones_WhenAutoPurgeDisabled_ReturnsEmptyAndLogsServerDetail()
    {
        // Arrange - the backend declines the fetch because auto-purge is off for this task hub. That is a
        // control transition the caller asked for, not a fault, so it must cost an empty cycle and nothing more.
        const string Detail = "Large-payload auto-purge is disabled for task hub 'test'.";
        TestLogger<GetLargePayloadTombstonesActivity> logger = new();
        LargePayloadPurgeClient client = new(
            new ThrowingCallInvoker(new RpcException(new Status(StatusCode.FailedPrecondition, Detail))));
        GetLargePayloadTombstonesActivity activity = new(client, logger);

        // Act
        List<LargePayloadTombstone> tombstones = await activity.RunAsync(null!, 100);

        // Assert - an empty batch routes the eternal orchestrator to its idle timer before it fetches again.
        tombstones.Should().BeEmpty();
        (LogLevel Level, string Message) entry = logger.Logs.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Information);
        entry.Message.Should().Contain(Detail);
        entry.Message.Should().Contain("precondition is not met");
        entry.Message.Should().Contain("No blobs are deleted from this response");
    }

    [Fact]
    public async Task GetLargePayloadTombstones_WhenTaskHubBeingDeleted_IsNotMislabelledAsDisabled()
    {
        // Arrange - the same status carries a different precondition. The backend answers FailedPrecondition
        // for a task hub that is being deleted too, so an SDK that hard-coded "auto-purge is disabled" here
        // would tell the customer the opposite of what happened.
        const string Detail = "Task hub is being deleted: test";
        TestLogger<GetLargePayloadTombstonesActivity> logger = new();
        LargePayloadPurgeClient client = new(
            new ThrowingCallInvoker(new RpcException(new Status(StatusCode.FailedPrecondition, Detail))));
        GetLargePayloadTombstonesActivity activity = new(client, logger);

        // Act
        List<LargePayloadTombstone> tombstones = await activity.RunAsync(null!, 100);

        // Assert - same safe no-op, and the server's detail is the only thing that names the cause. The
        // activity's own wording stays generic, so "disabled" can only appear here if it were mislabelled.
        tombstones.Should().BeEmpty();
        (LogLevel Level, string Message) entry = logger.Logs.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Information);
        entry.Message.Should().Contain(Detail);
        entry.Message.Should().Contain("precondition is not met");
        entry.Message.Should().NotContain("disabled");
    }

    [Fact]
    public async Task GetLargePayloadTombstones_WhenBackendCancels_ThrowsOperationCanceled()
    {
        // Arrange - cancellation is unrelated to the new precondition path and must keep its own translation.
        LargePayloadPurgeClient client = new(
            new ThrowingCallInvoker(new RpcException(new Status(StatusCode.Cancelled, "canceled"))));
        GetLargePayloadTombstonesActivity activity = new(client, new TestLogger<GetLargePayloadTombstonesActivity>());

        // Act
        Func<Task> act = () => activity.RunAsync(null!, 100);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetLargePayloadTombstones_WhenBackendFailsOtherwise_PropagatesRpcException()
    {
        // Arrange - a genuine backend fault. Only the three handled statuses are translated; everything else
        // must still surface as an RpcException so the activity retry policy gets its attempts.
        TestLogger<GetLargePayloadTombstonesActivity> logger = new();
        LargePayloadPurgeClient client = new(
            new ThrowingCallInvoker(new RpcException(new Status(StatusCode.Unavailable, "backend down"))));
        GetLargePayloadTombstonesActivity activity = new(client, logger);

        // Act
        Func<Task> act = () => activity.RunAsync(null!, 100);

        // Assert - and the precondition log must not fire for a status that is not a precondition failure.
        await act.Should().ThrowAsync<RpcException>()
            .Where(e => e.StatusCode == StatusCode.Unavailable);
        logger.Logs.Should().BeEmpty();
    }

    /// <summary>
    /// Reports retain their explicit backend-cancellation translation.
    /// </summary>
    [Fact]
    public async Task ReportLargePayloadPurgeResults_WhenBackendCancels_ThrowsOperationCanceledAsync()
    {
        // Arrange
        LargePayloadPurgeClient client = new(
            new ThrowingCallInvoker(new RpcException(new Status(StatusCode.Cancelled, "canceled"))));
        ReportLargePayloadPurgeResultsActivity activity =
            new(client, new TestLogger<ReportLargePayloadPurgeResultsActivity>());
        List<LargePayloadPurgeResult> results = new()
        {
            new LargePayloadPurgeResult("tombstone-token-1", LargePayloadPurgeDisposition.Deleted),
        };

        // Act
        Func<Task> act = () => activity.RunAsync(null!, results);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Report failures, including failed preconditions, remain retryable rather than reporting success.
    /// </summary>
    [Theory]
    [InlineData(StatusCode.FailedPrecondition)]
    [InlineData(StatusCode.Unavailable)]
    public async Task ReportLargePayloadPurgeResults_WhenBackendFailsOtherwise_PropagatesRpcExceptionAsync(StatusCode statusCode)
    {
        // Arrange
        RpcException error = new(new Status(statusCode, "backend failure"));
        LargePayloadPurgeClient client = new(new ThrowingCallInvoker(error));
        TestLogger<ReportLargePayloadPurgeResultsActivity> logger = new();
        ReportLargePayloadPurgeResultsActivity activity = new(client, logger);
        List<LargePayloadPurgeResult> results = new()
        {
            new LargePayloadPurgeResult("tombstone-token-1", LargePayloadPurgeDisposition.Deleted),
        };

        // Act
        Func<Task> act = () => activity.RunAsync(null!, results);

        // Assert
        (await act.Should().ThrowAsync<RpcException>()).Which.Should().BeSameAs(error);
        logger.Logs.Should().BeEmpty();
    }

    /// <summary>
    /// Fetch attempts use a UTC deadline and leave deadline failures available for activity retry.
    /// </summary>
    [Fact]
    public async Task GetLargePayloadTombstones_SetsDefaultUtcDeadlineAsync()
    {
        // Arrange
        RpcException error = new(new Status(StatusCode.DeadlineExceeded, "deadline exceeded"));
        ThrowingCallInvoker invoker = new(error);
        LargePayloadPurgeClient client = new(invoker);
        GetLargePayloadTombstonesActivity activity = new(client, new TestLogger<GetLargePayloadTombstonesActivity>());
        DateTime earliestDeadline = DateTime.UtcNow.AddSeconds(60);

        // Act
        Func<Task> act = () => activity.RunAsync(null!, 100);
        RpcException thrown = (await act.Should().ThrowAsync<RpcException>()).Which;
        DateTime latestDeadline = DateTime.UtcNow.AddSeconds(60);

        // Assert
        thrown.Should().BeSameAs(error);
        invoker.Options.Deadline.Should().NotBeNull();
        invoker.Options.Deadline!.Value.Kind.Should().Be(DateTimeKind.Utc);
        invoker.Options.Deadline.Value.Should().BeOnOrAfter(earliestDeadline).And.BeOnOrBefore(latestDeadline);
        invoker.IsDisposed.Should().BeTrue();
    }

    /// <summary>
    /// Report attempts use a UTC deadline and leave deadline failures available for activity retry.
    /// </summary>
    [Fact]
    public async Task ReportLargePayloadPurgeResults_SetsDefaultUtcDeadlineAsync()
    {
        // Arrange
        RpcException error = new(new Status(StatusCode.DeadlineExceeded, "deadline exceeded"));
        ThrowingCallInvoker invoker = new(error);
        LargePayloadPurgeClient client = new(invoker);
        ReportLargePayloadPurgeResultsActivity activity =
            new(client, new TestLogger<ReportLargePayloadPurgeResultsActivity>());
        List<LargePayloadPurgeResult> results = new()
        {
            new LargePayloadPurgeResult("tombstone-token-1", LargePayloadPurgeDisposition.Deleted),
        };
        DateTime earliestDeadline = DateTime.UtcNow.AddSeconds(60);

        // Act
        Func<Task> act = () => activity.RunAsync(null!, results);
        RpcException thrown = (await act.Should().ThrowAsync<RpcException>()).Which;
        DateTime latestDeadline = DateTime.UtcNow.AddSeconds(60);

        // Assert
        thrown.Should().BeSameAs(error);
        invoker.Options.Deadline.Should().NotBeNull();
        invoker.Options.Deadline!.Value.Kind.Should().Be(DateTimeKind.Utc);
        invoker.Options.Deadline.Value.Should().BeOnOrAfter(earliestDeadline).And.BeOnOrBefore(latestDeadline);
        invoker.IsDisposed.Should().BeTrue();
    }

    /// <summary>
    /// A minimal <see cref="CallInvoker"/> whose unary calls fault with a configured <see cref="RpcException"/>.
    /// Building a real <see cref="LargePayloadPurgeClient"/> over it exercises the activity's own catch
    /// clause exactly as a live channel would, without mocking the generated client. The faulted
    /// <see cref="AsyncUnaryCall{TResponse}"/> surfaces the exception on await, matching real gRPC.
    /// </summary>
    sealed class ThrowingCallInvoker(RpcException error) : CallInvoker
    {
        public CallOptions Options { get; private set; }

        public bool IsDisposed { get; private set; }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            this.Options = options;
            return new(
                Task.FromException<TResponse>(error),
                Task.FromResult(new Metadata()),
                () => error.Status,
                () => new Metadata(),
                () => this.IsDisposed = true);
        }

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
