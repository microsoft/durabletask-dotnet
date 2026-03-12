// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.DurableTask.Plugins;
using Microsoft.DurableTask.Plugins.BuiltIn;
using Xunit;

namespace Microsoft.DurableTask.Extensions.Plugins.Tests;

public class RateLimitingPluginTests
{
    [Fact]
    public async Task RateLimitingPlugin_AllowsWithinLimit()
    {
        // Arrange
        RateLimitingPlugin plugin = new(new RateLimitingOptions { MaxTokens = 5 });
        ActivityInterceptorContext context = new("TestActivity", "instance1", "input");

        // Act & Assert - should allow 5 calls
        for (int i = 0; i < 5; i++)
        {
            await plugin.ActivityInterceptors[0].Invoking(
                a => a.OnActivityStartingAsync(context))
                .Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task RateLimitingPlugin_DeniesExceedingLimit()
    {
        // Arrange
        RateLimitingPlugin plugin = new(new RateLimitingOptions
        {
            MaxTokens = 2,
            RefillRate = 0,
            RefillInterval = TimeSpan.FromHours(1),
        });
        ActivityInterceptorContext context = new("TestActivity", "instance1", "input");

        // Act - exhaust the tokens
        await plugin.ActivityInterceptors[0].OnActivityStartingAsync(context);
        await plugin.ActivityInterceptors[0].OnActivityStartingAsync(context);

        // Assert - should now throw
        await plugin.ActivityInterceptors[0].Invoking(
            a => a.OnActivityStartingAsync(context))
            .Should().ThrowAsync<RateLimitExceededException>()
            .WithMessage("*Rate limit exceeded*");
    }

    [Fact]
    public async Task RateLimitingPlugin_PerActivityName()
    {
        // Arrange
        RateLimitingPlugin plugin = new(new RateLimitingOptions
        {
            MaxTokens = 1,
            RefillRate = 0,
            RefillInterval = TimeSpan.FromHours(1),
        });
        ActivityInterceptorContext context1 = new("Activity1", "instance1", "input");
        ActivityInterceptorContext context2 = new("Activity2", "instance1", "input");

        // Act - consume token for Activity1
        await plugin.ActivityInterceptors[0].OnActivityStartingAsync(context1);

        // Assert - Activity2 should still have its own bucket
        await plugin.ActivityInterceptors[0].Invoking(
            a => a.OnActivityStartingAsync(context2))
            .Should().NotThrowAsync();

        // Assert - Activity1 is exhausted
        await plugin.ActivityInterceptors[0].Invoking(
            a => a.OnActivityStartingAsync(context1))
            .Should().ThrowAsync<RateLimitExceededException>();
    }

    [Fact]
    public void RateLimitingPlugin_HasNoOrchestrationInterceptors()
    {
        // Arrange & Act
        RateLimitingPlugin plugin = new(new RateLimitingOptions());

        // Assert
        plugin.OrchestrationInterceptors.Should().BeEmpty();
        plugin.ActivityInterceptors.Should().HaveCount(1);
    }
}
