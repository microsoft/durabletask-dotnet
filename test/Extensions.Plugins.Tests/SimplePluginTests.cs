// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.DurableTask.Plugins;
using Xunit;

namespace Microsoft.DurableTask.Extensions.Plugins.Tests;

public class SimplePluginTests
{
    [Fact]
    public void SimplePlugin_HasCorrectName()
    {
        // Arrange & Act
        SimplePlugin plugin = SimplePlugin.NewBuilder("MyOrg.TestPlugin").Build();

        // Assert
        plugin.Name.Should().Be("MyOrg.TestPlugin");
    }

    [Fact]
    public void SimplePlugin_RegistersTasks_DoesNotThrow()
    {
        // Arrange
        bool activityRegistered = false;
        bool orchestratorRegistered = false;

        SimplePlugin plugin = SimplePlugin.NewBuilder("MyOrg.TaskPlugin")
            .AddTasks(registry =>
            {
                registry.AddActivityFunc<string, string>("PluginActivity", (ctx, input) =>
                {
                    activityRegistered = true;
                    return $"processed: {input}";
                });
                registry.AddOrchestratorFunc("PluginOrchestration", ctx =>
                {
                    orchestratorRegistered = true;
                    return Task.FromResult<object?>("done");
                });
            })
            .Build();

        DurableTaskRegistry registry = new();

        // Act — should not throw
        plugin.Invoking(p => p.RegisterTasks(registry)).Should().NotThrow();

        // The registry accepted the registrations (no duplicate errors)
        // Registering the same names again should throw (proving first call succeeded)
        plugin.Invoking(p => p.RegisterTasks(registry)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SimplePlugin_NoTasks_RegisterTasksIsNoOp()
    {
        // Arrange
        SimplePlugin plugin = SimplePlugin.NewBuilder("MyOrg.InterceptorOnly").Build();
        DurableTaskRegistry registry = new();

        // Act — no-op, should not throw
        plugin.Invoking(p => p.RegisterTasks(registry)).Should().NotThrow();
    }

    [Fact]
    public void SimplePlugin_CombinesTasksAndInterceptors()
    {
        // Arrange
        Moq.Mock<IOrchestrationInterceptor> interceptor = new();

        SimplePlugin plugin = SimplePlugin.NewBuilder("MyOrg.FullPlugin")
            .AddTasks(registry =>
            {
                registry.AddActivityFunc<string, string>("PluginActivity", (ctx, input) => input);
            })
            .AddOrchestrationInterceptor(interceptor.Object)
            .Build();

        DurableTaskRegistry registry = new();

        // Act
        plugin.RegisterTasks(registry);

        // Assert — interceptors are separate from tasks
        plugin.OrchestrationInterceptors.Should().HaveCount(1);

        // Tasks were registered (attempting again should throw)
        plugin.Invoking(p => p.RegisterTasks(registry))
            .Should().Throw<ArgumentException>()
            .WithMessage("*PluginActivity*");
    }

    [Fact]
    public void SimplePlugin_MultipleAddTasks_RegistersAll()
    {
        // Arrange
        SimplePlugin plugin = SimplePlugin.NewBuilder("MyOrg.MultiPlugin")
            .AddTasks(registry =>
            {
                registry.AddActivityFunc<string, string>("Activity1", (ctx, input) => input);
            })
            .AddTasks(registry =>
            {
                registry.AddActivityFunc<string, string>("Activity2", (ctx, input) => input);
            })
            .Build();

        DurableTaskRegistry registry = new();

        // Act
        plugin.RegisterTasks(registry);

        // Assert — both activities are registered (re-registering would throw for both)
        Action reRegister = () => plugin.RegisterTasks(registry);
        reRegister.Should().Throw<ArgumentException>().WithMessage("*Activity1*");
    }
}
