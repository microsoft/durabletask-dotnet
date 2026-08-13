// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;
using Grpc.Core;
using Microsoft.DurableTask.Worker.Grpc;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

public class WorkItemStreamConsumerTests
{
    static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(150);

    [Fact]
    public async Task EmptyStream_ReturnsGracefulDrain()
    {
        WorkItemStreamResult result = await WorkItemStreamConsumer.ConsumeAsync(
            openStream: _ => EmptyStream(),
            silentDisconnectTimeout: TimeSpan.FromSeconds(5),
            onItem: _ => throw new InvalidOperationException("onItem should not be invoked"),
            onFirstMessage: () => throw new InvalidOperationException("onFirstMessage should not be invoked"),
            cancellation: CancellationToken.None);

        result.Outcome.Should().Be(WorkItemStreamOutcome.GracefulDrain);
        result.FirstMessageObserved.Should().BeFalse();
    }

    [Fact]
    public async Task StreamWithItems_ReturnsGracefulDrain_AndFiresCallbacks()
    {
        P.WorkItem item1 = new() { HealthPing = new P.HealthPing() };
        P.WorkItem item2 = new() { HealthPing = new P.HealthPing() };
        List<P.WorkItem> received = new();
        int firstMessageCount = 0;

        WorkItemStreamResult result = await WorkItemStreamConsumer.ConsumeAsync(
            openStream: _ => StreamOf(item1, item2),
            silentDisconnectTimeout: TimeSpan.FromSeconds(5),
            onItem: received.Add,
            onFirstMessage: () => firstMessageCount++,
            cancellation: CancellationToken.None);

        result.Outcome.Should().Be(WorkItemStreamOutcome.GracefulDrain);
        result.FirstMessageObserved.Should().BeTrue();
        received.Should().BeEquivalentTo(new[] { item1, item2 }, o => o.WithStrictOrdering());
        firstMessageCount.Should().Be(1);
    }

    [Fact]
    public async Task VeryLargeSilentDisconnectTimeout_IsClamped_AndStreamCanStillComplete()
    {
        WorkItemStreamResult result = await WorkItemStreamConsumer.ConsumeAsync(
            openStream: _ => EmptyStream(),
            silentDisconnectTimeout: TimeSpan.FromDays(365),
            onItem: _ => throw new InvalidOperationException("onItem should not be invoked"),
            onFirstMessage: null,
            cancellation: CancellationToken.None);

        result.Outcome.Should().Be(WorkItemStreamOutcome.GracefulDrain);
        result.FirstMessageObserved.Should().BeFalse();
    }

    [Fact]
    public async Task HangingStream_SurfacingOce_ReturnsSilentDisconnect()
    {
        WorkItemStreamResult result = await WorkItemStreamConsumer.ConsumeAsync(
            openStream: ct => HangingStream(ct, throwAsRpc: false),
            silentDisconnectTimeout: ShortTimeout,
            onItem: _ => { },
            onFirstMessage: null,
            cancellation: CancellationToken.None);

        result.Outcome.Should().Be(WorkItemStreamOutcome.SilentDisconnect);
        result.FirstMessageObserved.Should().BeFalse();
    }

    /// <summary>
    /// Regression test for the C1 silent-disconnect bug. grpc-dotnet by default surfaces a linked-token
    /// cancellation as <see cref="RpcException"/>(StatusCode.Cancelled), not <see cref="OperationCanceledException"/>.
    /// Pre-fix this exception propagated past the silent-disconnect branch and the channel-recreate
    /// callback was never invoked.
    /// </summary>
    [Fact]
    public async Task HangingStream_SurfacingRpcCancelled_ReturnsSilentDisconnect()
    {
        WorkItemStreamResult result = await WorkItemStreamConsumer.ConsumeAsync(
            openStream: ct => HangingStream(ct, throwAsRpc: true),
            silentDisconnectTimeout: ShortTimeout,
            onItem: _ => { },
            onFirstMessage: null,
            cancellation: CancellationToken.None);

        result.Outcome.Should().Be(WorkItemStreamOutcome.SilentDisconnect);
        result.FirstMessageObserved.Should().BeFalse();
    }

    [Fact]
    public async Task OuterCancellation_WithOceFromStream_ReturnsShutdown()
    {
        // When the inner stream surfaces cancellation as OperationCanceledException, the helper
        // classifies the termination and returns Shutdown.
        using CancellationTokenSource outer = new();
        outer.CancelAfter(ShortTimeout);

        WorkItemStreamResult result = await WorkItemStreamConsumer.ConsumeAsync(
            openStream: ct => HangingStream(ct, throwAsRpc: false),
            silentDisconnectTimeout: TimeSpan.FromSeconds(30),
            onItem: _ => { },
            onFirstMessage: null,
            cancellation: outer.Token);

        result.Outcome.Should().Be(WorkItemStreamOutcome.Shutdown);
        result.FirstMessageObserved.Should().BeFalse();
    }

    [Fact]
    public async Task OuterCancellation_WithRpcCancelledFromStream_PropagatesException()
    {
        // When the inner stream surfaces outer cancellation as RpcException(Cancelled), the helper
        // does NOT classify it as Shutdown — the caller's outer catch chain (ExecuteAsync) handles
        // RpcException(Cancelled)-during-shutdown. Adding it to the helper would conflict with the
        // post-fix silent-disconnect catch, which scopes RpcException(Cancelled) handling to the case
        // where the timeout source — not the outer cancellation — fired.
        using CancellationTokenSource outer = new();
        outer.CancelAfter(ShortTimeout);

        Func<Task> act = () => WorkItemStreamConsumer.ConsumeAsync(
            openStream: ct => HangingStream(ct, throwAsRpc: true),
            silentDisconnectTimeout: TimeSpan.FromSeconds(30),
            onItem: _ => { },
            onFirstMessage: null,
            cancellation: outer.Token);

        await act.Should().ThrowAsync<RpcException>().Where(e => e.StatusCode == StatusCode.Cancelled);
    }

    [Fact]
    public async Task PerItem_HeartbeatReset_KeepsTimerAlive()
    {
        // Proves the per-item timer reset -- not just a single arm at loop start -- is what keeps the
        // stream alive. Earlier versions of this test tried to prove the reset by racing real per-item
        // delays (each comfortably under the timeout) against the real silent-disconnect timeout (so
        // their sum comfortably exceeded it). That was still flaky under CI scheduling pressure: any
        // continuation between the "item processed" signal and the next write could be delayed by the
        // thread pool/scheduler, silently inflating an intended-short gap past the timeout even though
        // production was correct.
        //
        // This version removes wall-clock timing from the assertion entirely. ConsumeAsync exposes a
        // test-only observability hook that fires every time the silent-disconnect timer is (re-)armed:
        // once before the read loop starts, and once per item, immediately before that item is
        // dispatched to onItem. By recording the exact interleaving of "armed" and "item" events, the
        // test proves the structural guarantee directly -- an arm precedes every item, and the total arm
        // count is itemCount + 1 -- instead of inferring it from elapsed real time. A regression that
        // only arms the timer once at loop start (and never re-arms it per item) fails this assertion
        // deterministically, with no dependency on scheduler timing.
        const int itemCount = 5;
        List<string> events = new();
        int itemIndex = 0;

        P.WorkItem[] items = new P.WorkItem[itemCount];
        for (int i = 0; i < itemCount; i++)
        {
            items[i] = new P.WorkItem { HealthPing = new P.HealthPing() };
        }

        WorkItemStreamResult result = await WorkItemStreamConsumer.ConsumeAsync(
            openStream: _ => StreamOf(items),
            silentDisconnectTimeout: TimeSpan.FromMilliseconds(500),
            onItem: _ => events.Add($"item{itemIndex++}"),
            onFirstMessage: null,
            cancellation: CancellationToken.None,
            onSilentDisconnectTimerArmed: () => events.Add("armed"));

        result.Outcome.Should().Be(WorkItemStreamOutcome.GracefulDrain);
        result.FirstMessageObserved.Should().BeTrue();

        // 1 initial arm (before the loop starts) + 1 re-arm per item.
        events.Count(e => e == "armed").Should().Be(itemCount + 1);

        // Every item must be immediately preceded by its own re-arm, and the very first event overall
        // is the initial pre-loop arm.
        events[0].Should().Be("armed");
        for (int i = 0; i < itemCount; i++)
        {
            int armedIndex = 1 + (i * 2);
            events[armedIndex].Should().Be("armed", "item {0} must be preceded by a timer re-arm", i);
            events[armedIndex + 1].Should().Be($"item{i}");
        }
    }

    [Fact]
    public async Task UnrelatedRpcException_Propagates()
    {
        Func<Task> act = () => WorkItemStreamConsumer.ConsumeAsync(
            openStream: _ => ThrowingStream(new RpcException(new Status(StatusCode.Unavailable, "boom"))),
            silentDisconnectTimeout: TimeSpan.FromSeconds(5),
            onItem: _ => { },
            onFirstMessage: null,
            cancellation: CancellationToken.None);

        await act.Should().ThrowAsync<RpcException>().Where(e => e.StatusCode == StatusCode.Unavailable);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NonPositiveSilentDisconnectTimeout_OnlyShutdownEndsLoop(int timeoutMilliseconds)
    {
        // Arrange
        using CancellationTokenSource outer = new();
        outer.CancelAfter(ShortTimeout);

        // Act
        WorkItemStreamResult result = await WorkItemStreamConsumer.ConsumeAsync(
            openStream: ct => HangingStream(ct, throwAsRpc: false),
            silentDisconnectTimeout: TimeSpan.FromMilliseconds(timeoutMilliseconds),
            onItem: _ => { },
            onFirstMessage: null,
            cancellation: outer.Token);

        // Assert
        result.Outcome.Should().Be(WorkItemStreamOutcome.Shutdown);
    }

#pragma warning disable CS1998 // Async method lacks 'await' operators
    static async IAsyncEnumerable<P.WorkItem> EmptyStream()
    {
        yield break;
    }

    static async IAsyncEnumerable<P.WorkItem> StreamOf(params P.WorkItem[] items)
    {
        foreach (P.WorkItem item in items)
        {
            yield return item;
        }
    }

    static IAsyncEnumerable<P.WorkItem> ThrowingStream(Exception ex) => new ThrowingAsyncEnumerable(ex);
#pragma warning restore CS1998

    static async IAsyncEnumerable<P.WorkItem> HangingStream(
        [EnumeratorCancellation] CancellationToken ct,
        bool throwAsRpc)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException) when (throwAsRpc)
        {
            // Mimic grpc-dotnet's default surface shape for linked-token cancellation.
            throw new RpcException(new Status(StatusCode.Cancelled, "stream cancelled"));
        }

        yield break;
    }

    sealed class ThrowingAsyncEnumerable : IAsyncEnumerable<P.WorkItem>, IAsyncEnumerator<P.WorkItem>
    {
        readonly Exception exception;

        public ThrowingAsyncEnumerable(Exception exception)
        {
            this.exception = exception;
        }

        public P.WorkItem Current => throw new InvalidOperationException("No current item is available for a throwing stream.");

        public IAsyncEnumerator<P.WorkItem> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;

        public ValueTask DisposeAsync() => default;

        public ValueTask<bool> MoveNextAsync() => ValueTask.FromException<bool>(this.exception);
    }
}
