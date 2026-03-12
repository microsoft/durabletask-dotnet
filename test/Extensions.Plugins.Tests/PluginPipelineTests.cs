// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.DurableTask.Plugins;
using Microsoft.DurableTask.Plugins.BuiltIn;
using Moq;
using Xunit;

namespace Microsoft.DurableTask.Extensions.Plugins.Tests;

public class PluginPipelineTests
{
    [Fact]
    public async Task ExecuteOrchestrationStarting_InvokesAllInterceptors()
    {
        // Arrange
        Mock<IOrchestrationInterceptor> interceptor1 = new();
        Mock<IOrchestrationInterceptor> interceptor2 = new();

        SimplePlugin plugin = SimplePlugin.NewBuilder("test")
            .AddOrchestrationInterceptor(interceptor1.Object)
            .AddOrchestrationInterceptor(interceptor2.Object)
            .Build();

        PluginPipeline pipeline = new(new[] { plugin });
        OrchestrationInterceptorContext context = new("TestOrch", "instance1", false, null);

        // Act
        await pipeline.ExecuteOrchestrationStartingAsync(context);

        // Assert
        interceptor1.Verify(i => i.OnOrchestrationStartingAsync(context), Times.Once);
        interceptor2.Verify(i => i.OnOrchestrationStartingAsync(context), Times.Once);
    }

    [Fact]
    public async Task ExecuteActivityStarting_InvokesAllInterceptors()
    {
        // Arrange
        Mock<IActivityInterceptor> interceptor1 = new();
        Mock<IActivityInterceptor> interceptor2 = new();

        SimplePlugin plugin = SimplePlugin.NewBuilder("test")
            .AddActivityInterceptor(interceptor1.Object)
            .AddActivityInterceptor(interceptor2.Object)
            .Build();

        PluginPipeline pipeline = new(new[] { plugin });
        ActivityInterceptorContext context = new("TestActivity", "instance1", "input");

        // Act
        await pipeline.ExecuteActivityStartingAsync(context);

        // Assert
        interceptor1.Verify(i => i.OnActivityStartingAsync(context), Times.Once);
        interceptor2.Verify(i => i.OnActivityStartingAsync(context), Times.Once);
    }

    [Fact]
    public async Task ExecuteOrchestrationCompleted_InvokesAllInterceptors()
    {
        // Arrange
        Mock<IOrchestrationInterceptor> interceptor = new();

        SimplePlugin plugin = SimplePlugin.NewBuilder("test")
            .AddOrchestrationInterceptor(interceptor.Object)
            .Build();

        PluginPipeline pipeline = new(new[] { plugin });
        OrchestrationInterceptorContext context = new("TestOrch", "instance1", false, null);

        // Act
        await pipeline.ExecuteOrchestrationCompletedAsync(context, "result");

        // Assert
        interceptor.Verify(i => i.OnOrchestrationCompletedAsync(context, "result"), Times.Once);
    }

    [Fact]
    public async Task ExecuteOrchestrationFailed_InvokesAllInterceptors()
    {
        // Arrange
        Mock<IOrchestrationInterceptor> interceptor = new();
        Exception exception = new InvalidOperationException("test error");

        SimplePlugin plugin = SimplePlugin.NewBuilder("test")
            .AddOrchestrationInterceptor(interceptor.Object)
            .Build();

        PluginPipeline pipeline = new(new[] { plugin });
        OrchestrationInterceptorContext context = new("TestOrch", "instance1", false, null);

        // Act
        await pipeline.ExecuteOrchestrationFailedAsync(context, exception);

        // Assert
        interceptor.Verify(i => i.OnOrchestrationFailedAsync(context, exception), Times.Once);
    }

    [Fact]
    public async Task Pipeline_WithMultiplePlugins_InvokesInOrder()
    {
        // Arrange
        List<string> sequence = new();
        Mock<IOrchestrationInterceptor> interceptor1 = new();
        interceptor1.Setup(i => i.OnOrchestrationStartingAsync(It.IsAny<OrchestrationInterceptorContext>()))
            .Callback(() => sequence.Add("plugin1"))
            .Returns(Task.CompletedTask);

        Mock<IOrchestrationInterceptor> interceptor2 = new();
        interceptor2.Setup(i => i.OnOrchestrationStartingAsync(It.IsAny<OrchestrationInterceptorContext>()))
            .Callback(() => sequence.Add("plugin2"))
            .Returns(Task.CompletedTask);

        SimplePlugin plugin1 = SimplePlugin.NewBuilder("plugin1")
            .AddOrchestrationInterceptor(interceptor1.Object)
            .Build();
        SimplePlugin plugin2 = SimplePlugin.NewBuilder("plugin2")
            .AddOrchestrationInterceptor(interceptor2.Object)
            .Build();

        PluginPipeline pipeline = new(new IDurableTaskPlugin[] { plugin1, plugin2 });
        OrchestrationInterceptorContext context = new("TestOrch", "instance1", false, null);

        // Act
        await pipeline.ExecuteOrchestrationStartingAsync(context);

        // Assert
        sequence.Should().ContainInOrder("plugin1", "plugin2");
    }
}
