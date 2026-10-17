// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Testing;
using Microsoft.DurableTask.Worker;
using Xunit;

namespace InProcessTestHost.Tests;

/// <summary>
/// Tests purge date filtering through the public in-process test host client.
/// </summary>
public class PurgeInstancesTests
{
    /// <summary>
    /// Verifies that a completed instance can be purged without a creation time bound.
    /// </summary>
    [Fact]
    public async Task PurgeAllInstancesAsync_WithoutCreatedFrom_PurgesCompletedOrchestration()
    {
        // Arrange
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(tasks =>
        {
            tasks.AddOrchestratorFunc<string>("Complete", _ => Task.FromResult("done"));
        }, cancellationToken: timeout.Token);
        string instanceId = await host.Client.ScheduleNewOrchestrationInstanceAsync(
            "Complete", cancellation: timeout.Token);
        OrchestrationMetadata metadata = await host.Client.WaitForInstanceCompletionAsync(
            instanceId, cancellation: timeout.Token);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        PurgeInstancesFilter filter = new(Statuses: new[] { OrchestrationRuntimeStatus.Completed });

        // Act
        PurgeResult result = await host.Client.PurgeAllInstancesAsync(filter, cancellation: timeout.Token);

        // Assert
        Assert.Equal(1, result.PurgedInstanceCount);
        Assert.Null(await host.Client.GetInstanceAsync(instanceId, cancellation: timeout.Token));
    }

    /// <summary>
    /// Verifies that explicit lower bounds and upper-bound-only filters preserve date filtering.
    /// </summary>
    /// <param name="useCreatedFrom">Whether to specify the lower bound instead of the upper bound.</param>
    /// <param name="tickOffset">The bound's offset from the recorded creation time.</param>
    /// <param name="expectedCount">The expected number of purged instances.</param>
    [Theory]
    [InlineData(true, 0, 1)]
    [InlineData(true, 1, 0)]
    [InlineData(false, 0, 1)]
    [InlineData(false, -1, 0)]
    public async Task PurgeAllInstancesAsync_CreatedTimeBounds_RespectsBounds(
        bool useCreatedFrom, int tickOffset, int expectedCount)
    {
        // Arrange
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(tasks =>
        {
            tasks.AddOrchestratorFunc<string>("Complete", _ => Task.FromResult("done"));
        }, cancellationToken: timeout.Token);
        string instanceId = await host.Client.ScheduleNewOrchestrationInstanceAsync(
            "Complete", cancellation: timeout.Token);
        OrchestrationMetadata metadata = await host.Client.WaitForInstanceCompletionAsync(
            instanceId, cancellation: timeout.Token);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        DateTimeOffset bound = metadata.CreatedAt.AddTicks(tickOffset).ToOffset(TimeSpan.FromHours(2));
        PurgeInstancesFilter filter = new(
            CreatedFrom: useCreatedFrom ? bound : null,
            CreatedTo: useCreatedFrom ? null : bound,
            Statuses: new[] { OrchestrationRuntimeStatus.Completed });

        // Act
        PurgeResult result = await host.Client.PurgeAllInstancesAsync(filter, cancellation: timeout.Token);

        // Assert
        Assert.Equal(expectedCount, result.PurgedInstanceCount);
        OrchestrationMetadata? remaining = await host.Client.GetInstanceAsync(instanceId, cancellation: timeout.Token);
        if (expectedCount == 0)
        {
            Assert.NotNull(remaining);
            Assert.Equal(OrchestrationRuntimeStatus.Completed, remaining.RuntimeStatus);
        }
        else
        {
            Assert.Null(remaining);
        }
    }

    /// <summary>
    /// Verifies that an unbounded purge returns zero when no instances match.
    /// </summary>
    [Fact]
    public async Task PurgeAllInstancesAsync_WithoutInstances_ReturnsZero()
    {
        // Arrange
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(
            _ => { }, cancellationToken: timeout.Token);
        PurgeInstancesFilter filter = new(Statuses: new[] { OrchestrationRuntimeStatus.Completed });

        // Act
        PurgeResult result = await host.Client.PurgeAllInstancesAsync(filter, cancellation: timeout.Token);

        // Assert
        Assert.Equal(0, result.PurgedInstanceCount);
    }
}
