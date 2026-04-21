// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Threading.Channels;
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
        // Feed one item, wait ~2x the timeout, then complete. Without a heartbeat reset on the
        // delivered item the timer would fire mid-wait and the outcome would be SilentDisconnect.
        Channel<P.WorkItem> channel = Channel.CreateUnbounded<P.WorkItem>();
        TimeSpan timeout = TimeSpan.FromMilliseconds(200);

        Task<WorkItemStreamResult> consumeTask = WorkItemStreamConsumer.ConsumeAsync(
            openStream: ct => channel.Reader.ReadAllAsync(ct),
            silentDisconnectTimeout: timeout,
            onItem: _ => { },
            onFirstMessage: null,
            cancellation: CancellationToken.None);

        // Deliver an item just before each timer would fire to keep the stream "alive".
        await Task.Delay(timeout / 2);
        await channel.Writer.WriteAsync(new P.WorkItem { HealthPing = new P.HealthPing() });
        await Task.Delay(timeout / 2);
        await channel.Writer.WriteAsync(new P.WorkItem { HealthPing = new P.HealthPing() });
        channel.Writer.Complete();

        WorkItemStreamResult result = await consumeTask;

        result.Outcome.Should().Be(WorkItemStreamOutcome.GracefulDrain);
        result.FirstMessageObserved.Should().BeTrue();
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

    [Fact]
    public async Task DisabledSilentDisconnect_OnlyShutdownEndsLoop()
    {
        using CancellationTokenSource outer = new();
        outer.CancelAfter(ShortTimeout);

        WorkItemStreamResult result = await WorkItemStreamConsumer.ConsumeAsync(
            openStream: ct => HangingStream(ct, throwAsRpc: false),
            silentDisconnectTimeout: TimeSpan.Zero, // disabled
            onItem: _ => { },
            onFirstMessage: null,
            cancellation: outer.Token);

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

    static async IAsyncEnumerable<P.WorkItem> ThrowingStream(Exception ex)
    {
        throw ex;
#pragma warning disable CS0162 // Unreachable code detected
        yield break;
#pragma warning restore CS0162
    }
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
}
