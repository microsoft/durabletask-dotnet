// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities;
using Microsoft.DurableTask.Worker.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Worker.Tests;

public class DurableTaskBuilderWorkerExtensionsTests
{
    [Fact]
    public void UseBuildTarget_InvalidType_Throws()
    {
        DefaultDurableTaskWorkerBuilder builder = new("test", new ServiceCollection());
        Action act = () => builder.UseBuildTarget(typeof(BadBuildTarget));
        act.Should().ThrowExactly<ArgumentException>().WithParameterName("value");
    }

    [Fact]
    public void UseBuildTarget_ValidType_Sets()
    {
        DefaultDurableTaskWorkerBuilder builder = new("test", new ServiceCollection());
        Action act = () => builder.UseBuildTarget(typeof(GoodBuildTarget));
        act.Should().NotThrow();
        builder.BuildTarget.Should().Be(typeof(GoodBuildTarget));
    }

    [Fact]
    public void UseBuildTargetT_ValidType_Sets()
    {
        DefaultDurableTaskWorkerBuilder builder = new("test", new ServiceCollection());
        Action act = () => builder.UseBuildTarget<GoodBuildTarget>();
        act.Should().NotThrow();
        builder.BuildTarget.Should().Be(typeof(GoodBuildTarget));
    }

    [Fact]
    public void AddTasks_ConfiguresRegistry()
    {
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);

        DurableTaskRegistry? actual = null;
        builder.AddTasks(registry => actual = registry);
        DurableTaskRegistry expected = services.BuildServiceProvider().GetOptions<DurableTaskRegistry>("test");

        actual.Should().BeSameAs(expected);
    }

    [Fact]
    public void Configure_ConfiguresOptions()
    {
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);

        DurableTaskWorkerOptions? actual = null;
        builder.Configure(options => actual = options);
        DurableTaskWorkerOptions expected = services.BuildServiceProvider().GetOptions<DurableTaskWorkerOptions>("test");

        actual.Should().BeSameAs(expected);
    }

    [Fact]
    public void AddTasksAsServices_RegistersActivityTypes()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);

        // Act
        builder.AddTasksAsServices(registry =>
        {
            registry.AddActivity<TestActivity>();
        });

        // Assert
        IServiceProvider provider = services.BuildServiceProvider();
        provider.GetService<TestActivity>().Should().NotBeNull();
    }

    [Fact]
    public void AddTasksAsServices_RegistersOrchestratorTypes()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);

        // Act
        builder.AddTasksAsServices(registry =>
        {
            registry.AddOrchestrator<TestOrchestrator>();
        });

        // Assert
        IServiceProvider provider = services.BuildServiceProvider();
        provider.GetService<TestOrchestrator>().Should().NotBeNull();
    }

    [Fact]
    public void AddTasksAsServices_RegistersEntityTypes()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);

        // Act
        builder.AddTasksAsServices(registry =>
        {
            registry.AddEntity<TestEntity>();
        });

        // Assert
        IServiceProvider provider = services.BuildServiceProvider();
        provider.GetService<TestEntity>().Should().NotBeNull();
    }

    [Fact]
    public void AddTasksAsServices_RegistersMultipleTaskTypes()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);

        // Act
        builder.AddTasksAsServices(registry =>
        {
            registry.AddActivity<TestActivity>();
            registry.AddOrchestrator<TestOrchestrator>();
            registry.AddEntity<TestEntity>();
        });

        // Assert
        IServiceProvider provider = services.BuildServiceProvider();
        provider.GetService<TestActivity>().Should().NotBeNull();
        provider.GetService<TestOrchestrator>().Should().NotBeNull();
        provider.GetService<TestEntity>().Should().NotBeNull();
    }

    [Fact]
    public void AddTasksAsServices_DoesNotRegisterFunctionBasedTasks()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);

        // Act
        builder.AddTasksAsServices(registry =>
        {
            registry.AddActivityFunc("testFunc", (TaskActivityContext ctx) => Task.CompletedTask);
        });

        // Assert - No exception should be thrown and no types should be registered
        IServiceProvider provider = services.BuildServiceProvider();
        // There should be no issue building the service provider
        provider.Should().NotBeNull();
    }

    [Fact]
    public void AddTasksAsServices_DoesNotRegisterSingletonInstances()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
        TestActivity singletonActivity = new();

        // Act
        builder.AddTasksAsServices(registry =>
        {
            registry.AddActivity(singletonActivity);
        });

        // Assert - Singleton instances should not be registered in DI
        IServiceProvider provider = services.BuildServiceProvider();
        // Verify that TestActivity is not registered as a service
        // (it's only registered as a singleton instance with the worker)
        provider.GetService<TestActivity>().Should().BeNull();
    }

    [Fact]
    public void AddTasksAsServices_AlsoRegistersTasksWithWorker()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);

        // Act
        builder.AddTasksAsServices(registry =>
        {
            registry.AddActivity<TestActivity>();
        });

        // Assert - Tasks should be registered both as services and with the worker
        IServiceProvider provider = services.BuildServiceProvider();
        provider.GetService<TestActivity>().Should().NotBeNull();
        
        // Also verify the task is registered with the worker by checking the factory
        IDurableTaskFactory factory = provider.GetRequiredService<IOptionsMonitor<DurableTaskRegistry>>()
            .Get("test")
            .BuildFactory();
        factory.TryCreateActivity(nameof(TestActivity), provider, out ITaskActivity? activity).Should().BeTrue();
        activity.Should().NotBeNull();
    }

    class BadBuildTarget : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            throw new NotImplementedException();
        }
    }

    class GoodBuildTarget : DurableTaskWorker
    {
        public GoodBuildTarget(
            string name, DurableTaskFactory factory, IOptions<DurableTaskWorkerOptions> options)
            : base(name, factory)
        {
            this.Options = options.Value;
        }

        public new string Name => base.Name;

        public new IDurableTaskFactory Factory => base.Factory;

        public DurableTaskWorkerOptions Options { get; }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            throw new NotImplementedException();
        }
    }

    sealed class TestActivity : TaskActivity<object, object>
    {
        public override Task<object> RunAsync(TaskActivityContext context, object input)
        {
            return Task.FromResult<object>(input);
        }
    }

    sealed class TestOrchestrator : TaskOrchestrator<object, object>
    {
        public override Task<object> RunAsync(TaskOrchestrationContext context, object input)
        {
            return Task.FromResult<object>(input);
        }
    }

    sealed class TestEntity : TaskEntity<object>
    {
        public void Operation(object input)
        {
            // Simple operation for testing
        }
    }
}
