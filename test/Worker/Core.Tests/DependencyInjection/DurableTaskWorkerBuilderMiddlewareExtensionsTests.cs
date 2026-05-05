// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Microsoft.DurableTask.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Worker.Tests;

public class DurableTaskWorkerBuilderMiddlewareExtensionsTests
{
    [Fact]
    public void UseOrchestrationMiddlewareT_NullBuilder_Throws()
    {
        // Arrange
        IDurableTaskWorkerBuilder builder = null!;

        // Act
        Action act = () => builder.UseOrchestrationMiddleware<TestOrchestrationMiddleware>();

        // Assert
        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("builder");
    }

    [Fact]
    public void UseOrchestrationMiddlewareHandler_NullBuilder_Throws()
    {
        // Arrange
        IDurableTaskWorkerBuilder builder = null!;

        // Act
        Action act = () => builder.UseOrchestrationMiddleware(OrchestrationHandler);

        // Assert
        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("builder");
    }

    [Fact]
    public void UseActivityMiddlewareT_NullBuilder_Throws()
    {
        // Arrange
        IDurableTaskWorkerBuilder builder = null!;

        // Act
        Action act = () => builder.UseActivityMiddleware<TestActivityMiddleware>();

        // Assert
        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("builder");
    }

    [Fact]
    public void UseActivityMiddlewareHandler_NullBuilder_Throws()
    {
        // Arrange
        IDurableTaskWorkerBuilder builder = null!;

        // Act
        Action act = () => builder.UseActivityMiddleware(ActivityHandler);

        // Assert
        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("builder");
    }

    [Fact]
    public void UseOrchestrationMiddlewareHandler_NullHandler_Throws()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
        Func<TaskOrchestrationMiddlewareContext, TaskOrchestrationMiddlewareDelegate, Task> handler = null!;

        // Act
        Action act = () => builder.UseOrchestrationMiddleware(handler);

        // Assert
        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("handler");
    }

    [Fact]
    public void UseActivityMiddlewareHandler_NullHandler_Throws()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
        Func<TaskActivityMiddlewareContext, TaskActivityMiddlewareDelegate, Task> handler = null!;

        // Act
        Action act = () => builder.UseActivityMiddleware(handler);

        // Assert
        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("handler");
    }

    [Fact]
    public void UseOrchestrationMiddleware_ReturnsBuilder_ForChaining()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);

        // Act
        IDurableTaskWorkerBuilder typeResult = builder.UseOrchestrationMiddleware<TestOrchestrationMiddleware>();
        IDurableTaskWorkerBuilder handlerResult = builder.UseOrchestrationMiddleware(OrchestrationHandler);

        // Assert
        typeResult.Should().BeSameAs(builder);
        handlerResult.Should().BeSameAs(builder);
    }

    [Fact]
    public void UseActivityMiddleware_ReturnsBuilder_ForChaining()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);

        // Act
        IDurableTaskWorkerBuilder typeResult = builder.UseActivityMiddleware<TestActivityMiddleware>();
        IDurableTaskWorkerBuilder handlerResult = builder.UseActivityMiddleware(ActivityHandler);

        // Assert
        typeResult.Should().BeSameAs(builder);
        handlerResult.Should().BeSameAs(builder);
    }

    [Fact]
    public void UseOrchestrationMiddleware_StoresOrderedRegistrations()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
        Func<TaskOrchestrationMiddlewareContext, TaskOrchestrationMiddlewareDelegate, Task> handler =
            OrchestrationHandler;

        // Act
        builder.UseOrchestrationMiddleware<TestOrchestrationMiddleware>();
        builder.UseOrchestrationMiddleware(handler);
        builder.UseOrchestrationMiddleware<OtherOrchestrationMiddleware>();
        DurableTaskWorkerMiddlewareOptions options = GetMiddlewareOptions(services, "test");

        // Assert
        options.OrchestrationMiddleware.Should().HaveCount(3);
        options.OrchestrationMiddleware[0].MiddlewareType.Should().Be(typeof(TestOrchestrationMiddleware));
        options.OrchestrationMiddleware[0].Handler.Should().BeNull();
        options.OrchestrationMiddleware[1].MiddlewareType.Should().BeNull();
        options.OrchestrationMiddleware[1].Handler.Should().BeSameAs(handler);
        options.OrchestrationMiddleware[2].MiddlewareType.Should().Be(typeof(OtherOrchestrationMiddleware));
        options.OrchestrationMiddleware[2].Handler.Should().BeNull();
        options.ActivityMiddleware.Should().BeEmpty();
    }

    [Fact]
    public void UseActivityMiddleware_StoresOrderedRegistrations()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
        Func<TaskActivityMiddlewareContext, TaskActivityMiddlewareDelegate, Task> handler = ActivityHandler;

        // Act
        builder.UseActivityMiddleware<TestActivityMiddleware>();
        builder.UseActivityMiddleware(handler);
        builder.UseActivityMiddleware<OtherActivityMiddleware>();
        DurableTaskWorkerMiddlewareOptions options = GetMiddlewareOptions(services, "test");

        // Assert
        options.ActivityMiddleware.Should().HaveCount(3);
        options.ActivityMiddleware[0].MiddlewareType.Should().Be(typeof(TestActivityMiddleware));
        options.ActivityMiddleware[0].Handler.Should().BeNull();
        options.ActivityMiddleware[1].MiddlewareType.Should().BeNull();
        options.ActivityMiddleware[1].Handler.Should().BeSameAs(handler);
        options.ActivityMiddleware[2].MiddlewareType.Should().Be(typeof(OtherActivityMiddleware));
        options.ActivityMiddleware[2].Handler.Should().BeNull();
        options.OrchestrationMiddleware.Should().BeEmpty();
    }

    [Fact]
    public void UseMiddleware_NamedBuilders_AreIsolated()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder firstBuilder = new("first", services);
        DefaultDurableTaskWorkerBuilder secondBuilder = new("second", services);

        // Act
        firstBuilder.UseOrchestrationMiddleware<TestOrchestrationMiddleware>();
        firstBuilder.UseActivityMiddleware<TestActivityMiddleware>();
        secondBuilder.UseOrchestrationMiddleware<OtherOrchestrationMiddleware>();
        DurableTaskWorkerMiddlewareOptions firstOptions = GetMiddlewareOptions(services, "first");
        DurableTaskWorkerMiddlewareOptions secondOptions = GetMiddlewareOptions(services, "second");

        // Assert
        firstOptions.OrchestrationMiddleware.Should().ContainSingle()
            .Which.MiddlewareType.Should().Be(typeof(TestOrchestrationMiddleware));
        firstOptions.ActivityMiddleware.Should().ContainSingle()
            .Which.MiddlewareType.Should().Be(typeof(TestActivityMiddleware));
        secondOptions.OrchestrationMiddleware.Should().ContainSingle()
            .Which.MiddlewareType.Should().Be(typeof(OtherOrchestrationMiddleware));
        secondOptions.ActivityMiddleware.Should().BeEmpty();
    }

    [Fact]
    public void UseMiddlewareT_RegistersConcreteTypesAsResolvableServices()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);

        // Act
        builder.UseOrchestrationMiddleware<TestOrchestrationMiddleware>();
        builder.UseActivityMiddleware<TestActivityMiddleware>();
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        // Assert
        scope.ServiceProvider.GetRequiredService<TestOrchestrationMiddleware>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<TestActivityMiddleware>().Should().NotBeNull();
    }

    [Fact]
    public void UseMiddlewareT_DoesNotOverrideExistingRegistrations()
    {
        // Arrange
        TestOrchestrationMiddleware orchestrationMiddleware = new();
        TestActivityMiddleware activityMiddleware = new();
        ServiceCollection services = new();
        services.AddSingleton(orchestrationMiddleware);
        services.AddSingleton(activityMiddleware);
        DefaultDurableTaskWorkerBuilder builder = new("test", services);

        // Act
        builder.UseOrchestrationMiddleware<TestOrchestrationMiddleware>();
        builder.UseActivityMiddleware<TestActivityMiddleware>();
        using ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        provider.GetRequiredService<TestOrchestrationMiddleware>().Should().BeSameAs(orchestrationMiddleware);
        provider.GetRequiredService<TestActivityMiddleware>().Should().BeSameAs(activityMiddleware);
    }

    [Fact]
    public void UseMiddlewareT_AbstractType_Throws()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);

        // Act
        Action orchestrationAct = () => builder.UseOrchestrationMiddleware<AbstractOrchestrationMiddleware>();
        Action activityAct = () => builder.UseActivityMiddleware<AbstractActivityMiddleware>();

        // Assert
        orchestrationAct.Should().ThrowExactly<ArgumentException>();
        activityAct.Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void DurableTaskWorkerBuilderExtensions_DoesNotExposeEntityMiddlewareRegistration()
    {
        // Arrange
        MethodInfo[] methods = typeof(DurableTaskWorkerBuilderExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static);

        // Act
        IEnumerable<MethodInfo> entityMiddlewareMethods = methods
            .Where(method => method.Name.Contains("EntityMiddleware", StringComparison.Ordinal));

        // Assert
        entityMiddlewareMethods.Should().BeEmpty();
    }

    static DurableTaskWorkerMiddlewareOptions GetMiddlewareOptions(IServiceCollection services, string name)
    {
        ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerMiddlewareOptions>>().Get(name);
    }

    static Task OrchestrationHandler(
        TaskOrchestrationMiddlewareContext context, TaskOrchestrationMiddlewareDelegate next)
        => next(context);

    static Task ActivityHandler(TaskActivityMiddlewareContext context, TaskActivityMiddlewareDelegate next)
        => next(context);

    sealed class TestOrchestrationMiddleware : ITaskOrchestrationMiddleware
    {
        public Task InvokeAsync(
            TaskOrchestrationMiddlewareContext context, TaskOrchestrationMiddlewareDelegate next)
            => next(context);
    }

    sealed class OtherOrchestrationMiddleware : ITaskOrchestrationMiddleware
    {
        public Task InvokeAsync(
            TaskOrchestrationMiddlewareContext context, TaskOrchestrationMiddlewareDelegate next)
            => next(context);
    }

    abstract class AbstractOrchestrationMiddleware : ITaskOrchestrationMiddleware
    {
        public abstract Task InvokeAsync(
            TaskOrchestrationMiddlewareContext context, TaskOrchestrationMiddlewareDelegate next);
    }

    sealed class TestActivityMiddleware : ITaskActivityMiddleware
    {
        public Task InvokeAsync(TaskActivityMiddlewareContext context, TaskActivityMiddlewareDelegate next)
            => next(context);
    }

    sealed class OtherActivityMiddleware : ITaskActivityMiddleware
    {
        public Task InvokeAsync(TaskActivityMiddlewareContext context, TaskActivityMiddlewareDelegate next)
            => next(context);
    }

    abstract class AbstractActivityMiddleware : ITaskActivityMiddleware
    {
        public abstract Task InvokeAsync(TaskActivityMiddlewareContext context, TaskActivityMiddlewareDelegate next);
    }
}
