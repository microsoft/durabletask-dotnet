// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Tests.Logging;
using Microsoft.DurableTask.Worker;
using Xunit.Abstractions;

namespace Microsoft.DurableTask.Grpc.Tests;

/// <summary>
/// Integration tests for external event handling with Stack (LIFO) behavior.
/// These tests validate that the isolated worker correctly implements Stack-based
/// event handling consistent with the in-process model.
/// </summary>
public class ExternalEventStackTests : IntegrationTestBase
{
    public ExternalEventStackTests(ITestOutputHelper output, GrpcSidecarFixture sidecarFixture)
        : base(output, sidecarFixture)
    { }

    /// <summary>
    /// Test that validates Stack (LIFO) behavior: the most recent waiter receives events first.
    /// </summary>
    [Fact]
    public async Task StackBehavior_LIFO_NewestWaiterReceivesEventFirst()
    {
        const string EventName = "TestEvent";
        const string FirstEventPayload = "first-event";
        const string SecondEventPayload = "second-event";
        TaskName orchestratorName = nameof(StackBehavior_LIFO_NewestWaiterReceivesEventFirst);

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc(orchestratorName, async ctx =>
            {
                // First waiter
                Task<string> firstWaiter = ctx.WaitForExternalEvent<string>(EventName);
                
                // Second waiter (newer, should receive event first)
                Task<string> secondWaiter = ctx.WaitForExternalEvent<string>(EventName);
                
                // Wait for both events to arrive from external client
                string secondResult = await secondWaiter; // Should receive first event (stack top)
                string firstResult = await firstWaiter;   // Should receive second event
                
                // Return which waiter received which event
                return $"{firstResult}:{secondResult}";
            }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        
        // Wait for orchestration to start and set up waiters
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceStartAsync(
            instanceId, this.TimeoutToken);
        
        // Send first event - should be received by second waiter (stack top)
        await server.Client.RaiseEventAsync(instanceId, EventName, SecondEventPayload);
        
        // Send second event - should be received by first waiter
        await server.Client.RaiseEventAsync(instanceId, EventName, FirstEventPayload);

        metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        
        // Verify the output: second waiter should have received "second-event", first waiter "first-event"
        string result = metadata.ReadOutputAs<string>();
        Assert.Equal($"{FirstEventPayload}:{SecondEventPayload}", result);
    }

    /// <summary>
    /// Test for issue #508: When the first waiter is cancelled, the second (active) waiter
    /// should receive the event, not the cancelled one. With Stack behavior, the newest waiter
    /// is at the top, so it will receive the event first.
    /// </summary>
    [Fact]
    public async Task Issue508_FirstWaiterCancelled_SecondWaiterReceivesEvent()
    {
        const string EventName = "Event";
        const string EventPayload = "test-payload";
        TaskName orchestratorName = nameof(Issue508_FirstWaiterCancelled_SecondWaiterReceivesEvent);

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc(orchestratorName, async ctx =>
            {
                // STEP 1: Create and immediately cancel the first waiter
                using (CancellationTokenSource cts = new CancellationTokenSource())
                {
                    cts.Cancel();
                    try
                    {
                        Task<string> cancelledWait = ctx.WaitForExternalEvent<string>(EventName, cts.Token);
                        await cancelledWait;
                        throw new InvalidOperationException("WaitForExternalEvent should have thrown cancellation exception");
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected path
                    }
                }

                // STEP 2: Create second waiter (active, should receive the event)
                // With Stack (LIFO), this new waiter is at the top, so it will receive the event
                using (CancellationTokenSource cts = new CancellationTokenSource())
                {
                    Task<string> waitForEvent = ctx.WaitForExternalEvent<string>(EventName, cts.Token);
                    Task timeout = ctx.CreateTimer(ctx.CurrentUtcDateTime.AddSeconds(2), CancellationToken.None);
                    
                    Task winner = await Task.WhenAny(waitForEvent, timeout);
                    if (winner == timeout)
                    {
                        cts.Cancel();
                        throw new TimeoutException("Event lost: WaitForExternalEvent timed out");
                    }

                    string result = await waitForEvent;
                    return result;
                }
            }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        
        // Wait for orchestration to start and cancel first waiter
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceStartAsync(
            instanceId, this.TimeoutToken);
        
        // Send event - should be received by second waiter (stack top), not the cancelled first waiter
        await server.Client.RaiseEventAsync(instanceId, EventName, EventPayload);

        metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        
        string result = metadata.ReadOutputAs<string>();
        Assert.Equal(EventPayload, result);
    }

    /// <summary>
    /// Test multiple events with multiple waiters: validates that events are delivered
    /// in LIFO order to waiters.
    /// </summary>
    [Fact]
    public async Task MultipleEvents_MultipleWaiters_LIFOOrder()
    {
        const string EventName = "Event";
        TaskName orchestratorName = nameof(MultipleEvents_MultipleWaiters_LIFOOrder);

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc(orchestratorName, async ctx =>
            {
                // Create three waiters
                Task<string> waiter1 = ctx.WaitForExternalEvent<string>(EventName);
                Task<string> waiter2 = ctx.WaitForExternalEvent<string>(EventName);
                Task<string> waiter3 = ctx.WaitForExternalEvent<string>(EventName);

                // Wait for all events
                string result3 = await waiter3; // Should get first event (stack top)
                string result2 = await waiter2; // Should get second event
                string result1 = await waiter1; // Should get third event (oldest)

                // Return as comma-separated string for easier assertion
                return $"{result1},{result2},{result3}";
            }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        
        // Wait for orchestration to start and set up waiters
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceStartAsync(
            instanceId, this.TimeoutToken);
        
        // Send three events - should be received in LIFO order
        await server.Client.RaiseEventAsync(instanceId, EventName, "event-1");
        await server.Client.RaiseEventAsync(instanceId, EventName, "event-2");
        await server.Client.RaiseEventAsync(instanceId, EventName, "event-3");

        metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        
        // Verify LIFO order: waiter3 (newest) gets event-1, waiter2 gets event-2, waiter1 gets event-3
        string result = metadata.ReadOutputAs<string>();
        Assert.Equal("event-3,event-2,event-1", result);
    }

    /// <summary>
    /// Test event buffering: when an event arrives before any waiters, it should be buffered
    /// and delivered to the first waiter that arrives.
    /// </summary>
    [Fact]
    public async Task EventBuffering_EventArrivesBeforeWaiter_FirstWaiterReceivesBufferedEvent()
    {
        const string EventName = "Event";
        const string EventPayload = "buffered-event";
        TaskName orchestratorName = nameof(EventBuffering_EventArrivesBeforeWaiter_FirstWaiterReceivesBufferedEvent);

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc(orchestratorName, async ctx =>
            {
                // Small delay to allow external event to arrive first
                await ctx.CreateTimer(ctx.CurrentUtcDateTime.AddMilliseconds(100), CancellationToken.None);

                // Now create waiter - should immediately receive the buffered event
                Task<string> waiter = ctx.WaitForExternalEvent<string>(EventName);
                Task timeout = ctx.CreateTimer(ctx.CurrentUtcDateTime.AddSeconds(1), CancellationToken.None);
                
                Task winner = await Task.WhenAny(waiter, timeout);
                if (winner == timeout)
                {
                    throw new TimeoutException("Buffered event was not received");
                }

                string result = await waiter;
                return result;
            }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        
        // Send event before waiter is created
        await server.Client.RaiseEventAsync(instanceId, EventName, EventPayload);

        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        
        string result = metadata.ReadOutputAs<string>();
        Assert.Equal(EventPayload, result);
    }
}
