// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.DurableTask.Plugins;
using Microsoft.DurableTask.Plugins.BuiltIn;
using Xunit;

namespace Microsoft.DurableTask.Extensions.Plugins.Tests;

public class MetricsPluginTests
{
    [Fact]
    public void MetricsPlugin_HasCorrectName()
    {
        // Arrange & Act
        MetricsPlugin plugin = new();

        // Assert
        plugin.Name.Should().Be(MetricsPlugin.DefaultName);
    }

    [Fact]
    public async Task MetricsPlugin_TracksOrchestrationStarted()
    {
        // Arrange
        MetricsStore store = new();
        MetricsPlugin plugin = new(store);
        OrchestrationInterceptorContext context = new("TestOrch", "instance1", false, null);

        // Act
        await plugin.OrchestrationInterceptors[0].OnOrchestrationStartingAsync(context);

        // Assert
        store.GetOrchestrationMetrics("TestOrch").Started.Should().Be(1);
    }

    [Fact]
    public async Task MetricsPlugin_TracksOrchestrationCompleted()
    {
        // Arrange
        MetricsStore store = new();
        MetricsPlugin plugin = new(store);
        OrchestrationInterceptorContext context = new("TestOrch", "instance1", false, null);

        // Act
        await plugin.OrchestrationInterceptors[0].OnOrchestrationStartingAsync(context);
        await plugin.OrchestrationInterceptors[0].OnOrchestrationCompletedAsync(context, "result");

        // Assert
        store.GetOrchestrationMetrics("TestOrch").Completed.Should().Be(1);
        store.GetOrchestrationMetrics("TestOrch").TotalDuration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task MetricsPlugin_TracksOrchestrationFailed()
    {
        // Arrange
        MetricsStore store = new();
        MetricsPlugin plugin = new(store);
        OrchestrationInterceptorContext context = new("TestOrch", "instance1", false, null);
        Exception exception = new InvalidOperationException("test");

        // Act
        await plugin.OrchestrationInterceptors[0].OnOrchestrationStartingAsync(context);
        await plugin.OrchestrationInterceptors[0].OnOrchestrationFailedAsync(context, exception);

        // Assert
        store.GetOrchestrationMetrics("TestOrch").Failed.Should().Be(1);
    }

    [Fact]
    public async Task MetricsPlugin_TracksActivityLifecycle()
    {
        // Arrange
        MetricsStore store = new();
        MetricsPlugin plugin = new(store);
        ActivityInterceptorContext context = new("TestActivity", "instance1", "input");

        // Act
        await plugin.ActivityInterceptors[0].OnActivityStartingAsync(context);
        await plugin.ActivityInterceptors[0].OnActivityCompletedAsync(context, "result");

        // Assert
        store.GetActivityMetrics("TestActivity").Started.Should().Be(1);
        store.GetActivityMetrics("TestActivity").Completed.Should().Be(1);
    }
}
