// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.DependencyInjection;

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
        DurableTaskWorkerWorkItemFilters actual = provider.GetRequiredService<DurableTaskWorkerWorkItemFilters>();

        // Assert
        actual.Should().BeSameAs(filters);
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
        DurableTaskWorkerWorkItemFilters actual = provider.GetRequiredService<DurableTaskWorkerWorkItemFilters>();

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
                DefaultVersion = "1.0"
            };
        });

        // Act
        builder.UseWorkItemFilters();
        ServiceProvider provider = services.BuildServiceProvider();
        DurableTaskWorkerWorkItemFilters actual = provider.GetRequiredService<DurableTaskWorkerWorkItemFilters>();

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
        DurableTaskWorkerWorkItemFilters actual = provider.GetRequiredService<DurableTaskWorkerWorkItemFilters>();

        // Assert
        actual.Entities.Should().ContainSingle(e => e.Name == nameof(TestEntity).ToLowerInvariant());
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
        DurableTaskWorkerWorkItemFilters actual = provider.GetRequiredService<DurableTaskWorkerWorkItemFilters>();

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
        IEnumerable<DurableTaskWorkerWorkItemFilters> allFilters = provider.GetServices<DurableTaskWorkerWorkItemFilters>();

        // Assert
        allFilters.Should().HaveCount(2);
        allFilters.Should().Contain(f => f.Orchestrations.Any(o => o.Name == nameof(TestOrchestrator)) && !f.Activities.Any());
        allFilters.Should().Contain(f => f.Activities.Any(a => a.Name == nameof(TestActivity)) && !f.Orchestrations.Any());
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
