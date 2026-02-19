// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Worker.Tests;

public class UseWorkItemFiltersTests
{
    [Fact]
    public void UseWorkItemFilters_NullBuilder_Throws()
    {
        // Arrange
        IDurableTaskWorkerBuilder builder = null!;

        // Act
        Action act = () => builder.UseWorkItemFilters();

        // Assert
        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("builder");
    }

    [Fact]
    public void UseWorkItemFilters_WithExplicitFilters_RegistersFilters()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
        DurableTaskRegistry registry = new();
        DurableTaskWorkerWorkItemFilters filters = DurableTaskWorkerWorkItemFilters.FromDurableTaskRegistry(registry, null);

        // Act
        builder.UseWorkItemFilters(filters);
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();
        DurableTaskWorkerWorkItemFilters actual = filtersMonitor.Get("test");

        // Assert
        actual.Orchestrations.Should().BeEquivalentTo(filters.Orchestrations);
        actual.Activities.Should().BeEquivalentTo(filters.Activities);
        actual.Entities.Should().BeEquivalentTo(filters.Entities);
    }

    [Fact]
    public void UseWorkItemFilters_WithoutFilters_AutoGeneratesFromRegistry()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
        builder.AddTasks(registry =>
        {
            registry.AddOrchestrator<TestOrchestrator>();
            registry.AddActivity<TestActivity>();
        });

        // Act
        builder.UseWorkItemFilters();
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();
        DurableTaskWorkerWorkItemFilters actual = filtersMonitor.Get("test");

        // Assert
        actual.Orchestrations.Should().ContainSingle(o => o.Name == nameof(TestOrchestrator));
        actual.Activities.Should().ContainSingle(a => a.Name == nameof(TestActivity));
    }

    [Fact]
    public void UseWorkItemFilters_WithVersioning_IncludesVersionInFilters()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
        builder.AddTasks(registry =>
        {
            registry.AddOrchestrator<TestOrchestrator>();
            registry.AddActivity<TestActivity>();
        });
        builder.Configure(options =>
        {
            options.Versioning = new DurableTaskWorkerOptions.VersioningOptions
            {
                Version = "1.0"
            };
        });

        // Act
        builder.UseWorkItemFilters();
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();
        DurableTaskWorkerWorkItemFilters actual = filtersMonitor.Get("test");

        // Assert
        actual.Orchestrations.Should().ContainSingle(o => o.Name == nameof(TestOrchestrator) && o.Versions.Contains("1.0"));
        actual.Activities.Should().ContainSingle(a => a.Name == nameof(TestActivity) && a.Versions.Contains("1.0"));
    }

    [Fact]
    public void UseWorkItemFilters_WithEntity_IncludesEntityInFilters()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
        builder.AddTasks(registry =>
        {
            registry.AddEntity<TestEntity>();
        });

        // Act
        builder.UseWorkItemFilters();
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();
        DurableTaskWorkerWorkItemFilters actual = filtersMonitor.Get("test");

        // Assert
        actual.Entities.Should().ContainSingle(e => e.Name == nameof(TestEntity));
    }

    [Fact]
    public void UseWorkItemFilters_ReturnsBuilder_ForChaining()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);

        // Act
        IDurableTaskWorkerBuilder result = builder.UseWorkItemFilters();

        // Assert
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void UseWorkItemFilters_EmptyRegistry_CreatesEmptyFilters()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
        builder.AddTasks(_ => { });

        // Act
        builder.UseWorkItemFilters();
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();
        DurableTaskWorkerWorkItemFilters actual = filtersMonitor.Get("test");

        // Assert
        actual.Orchestrations.Should().BeEmpty();
        actual.Activities.Should().BeEmpty();
        actual.Entities.Should().BeEmpty();
    }

    [Fact]
    public void UseWorkItemFilters_NamedBuilders_HaveUniqueFilters()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder1 = new("worker1", services);
        builder1.AddTasks(registry => registry.AddOrchestrator<TestOrchestrator>());
        builder1.UseWorkItemFilters();

        DefaultDurableTaskWorkerBuilder builder2 = new("worker2", services);
        builder2.AddTasks(registry => registry.AddActivity<TestActivity>());
        builder2.UseWorkItemFilters();

        // Act
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();

        DurableTaskWorkerWorkItemFilters worker1Filters = filtersMonitor.Get("worker1");
        DurableTaskWorkerWorkItemFilters worker2Filters = filtersMonitor.Get("worker2");

        // Assert
        worker1Filters.Orchestrations.Should().ContainSingle(o => o.Name == nameof(TestOrchestrator));
        worker1Filters.Activities.Should().BeEmpty();

        worker2Filters.Activities.Should().ContainSingle(a => a.Name == nameof(TestActivity));
        worker2Filters.Orchestrations.Should().BeEmpty();
    }

    [Fact]
    public void UseWorkItemFilters_NamedBuilders_CanResolveCorrectFiltersByName()
    {
        // Arrange
        // This test verifies that named builders can have their filters resolved independently,
        // which is how the actual GrpcDurableTaskWorker needs to resolve filters for each named worker.
        ServiceCollection services = new();

        DefaultDurableTaskWorkerBuilder builder1 = new("worker1", services);
        builder1.AddTasks(registry => registry.AddOrchestrator<TestOrchestrator>());
        builder1.UseWorkItemFilters();

        DefaultDurableTaskWorkerBuilder builder2 = new("worker2", services);
        builder2.AddTasks(registry => registry.AddActivity<TestActivity>());
        builder2.UseWorkItemFilters();

        // Act
        ServiceProvider provider = services.BuildServiceProvider();

        // Use the options pattern to get filters by name - this is how the worker should resolve filters
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();

        DurableTaskWorkerWorkItemFilters worker1Filters = filtersMonitor.Get("worker1");
        DurableTaskWorkerWorkItemFilters worker2Filters = filtersMonitor.Get("worker2");

        // Assert
        // Worker1 should have orchestrator but no activity
        worker1Filters.Orchestrations.Should().ContainSingle(o => o.Name == nameof(TestOrchestrator));
        worker1Filters.Activities.Should().BeEmpty();

        // Worker2 should have activity but no orchestrator
        worker2Filters.Activities.Should().ContainSingle(a => a.Name == nameof(TestActivity));
        worker2Filters.Orchestrations.Should().BeEmpty();

        // The two filters should be different instances
        worker1Filters.Should().NotBeSameAs(worker2Filters);
    }

    class TestOrchestrator : TaskOrchestrator<object, object>
    {
        public override Task<object> RunAsync(TaskOrchestrationContext context, object input)
        {
            throw new NotImplementedException();
        }
    }

    class TestActivity : TaskActivity<object, object>
    {
        public override Task<object> RunAsync(TaskActivityContext context, object input)
        {
            throw new NotImplementedException();
        }
    }

    class TestEntity : TaskEntity<object>
    {
    }
}
