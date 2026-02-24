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
        Action act = () => builder.UseWorkItemFilters(null);

        // Assert
        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("builder");
    }

    [Fact]
    public void UseWorkItemFilters_WithExplicitFilters_RegistersFilters()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
        DurableTaskWorkerWorkItemFilters filters = new()
        {
            Orchestrations = [new DurableTaskWorkerWorkItemFilters.OrchestrationFilter { Name = "MyOrch", Versions = ["1.0"] }],
            Activities = [new DurableTaskWorkerWorkItemFilters.ActivityFilter { Name = "MyActivity", Versions = [] }],
            Entities = [new DurableTaskWorkerWorkItemFilters.EntityFilter { Name = "myentity" }],
        };

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
    public void UseWorkItemFilters_ReturnsBuilder_ForChaining()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);

        // Act
        IDurableTaskWorkerBuilder result = builder.UseWorkItemFilters(null);

        // Assert
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void WorkItemFilters_DefaultFromRegistry_WhenNoExplicitFiltersConfigured()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry =>
            {
                registry.AddOrchestrator<TestOrchestrator>();
                registry.AddActivity<TestActivity>();
            });
        });

        // Act
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();
        DurableTaskWorkerWorkItemFilters actual = filtersMonitor.Get("test");

        // Assert
        actual.Orchestrations.Should().ContainSingle(o => o.Name == nameof(TestOrchestrator));
        actual.Activities.Should().ContainSingle(a => a.Name == nameof(TestActivity));
    }

    [Fact]
    public void WorkItemFilters_DefaultWithEntity_WhenNoExplicitFiltersConfigured()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry =>
            {
                registry.AddEntity<TestEntity>();
            });
        });

        // Act
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();
        DurableTaskWorkerWorkItemFilters actual = filtersMonitor.Get("test");

        // Assert
        actual.Entities.Should().ContainSingle(e => e.Name == nameof(TestEntity));
    }

    [Fact]
    public void WorkItemFilters_DefaultNullWithVersioningCurrentOrOlder_WhenNoExplicitFiltersConfigured()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry =>
            {
                registry.AddOrchestrator<TestOrchestrator>();
                registry.AddActivity<TestActivity>();
            });
            builder.Configure(options =>
            {
                options.Versioning = new DurableTaskWorkerOptions.VersioningOptions
                {
                    Version = "1.0",
                    MatchStrategy = DurableTaskWorkerOptions.VersionMatchStrategy.CurrentOrOlder,
                };
            });
        });

        // Act
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();
        DurableTaskWorkerWorkItemFilters actual = filtersMonitor.Get("test");

        // Assert
        actual.Orchestrations.Should().ContainSingle(o => o.Name == nameof(TestOrchestrator) && o.Versions.Count == 0);
        actual.Activities.Should().ContainSingle(a => a.Name == nameof(TestActivity) && a.Versions.Count == 0);
    }

    [Fact]
    public void WorkItemFilters_DefaultNullWithVersioningNone_WhenNoExplicitFiltersConfigured()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry =>
            {
                registry.AddOrchestrator<TestOrchestrator>();
                registry.AddActivity<TestActivity>();
            });
            builder.Configure(options =>
            {
                options.Versioning = new DurableTaskWorkerOptions.VersioningOptions
                {
                    Version = "1.0",
                    MatchStrategy = DurableTaskWorkerOptions.VersionMatchStrategy.None,
                };
            });
        });

        // Act
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();
        DurableTaskWorkerWorkItemFilters actual = filtersMonitor.Get("test");

        // Assert
        actual.Orchestrations.Should().ContainSingle(o => o.Name == nameof(TestOrchestrator) && o.Versions.Count == 0);
        actual.Activities.Should().ContainSingle(a => a.Name == nameof(TestActivity) && a.Versions.Count == 0);
    }

    [Fact]
    public void WorkItemFilters_DefaultVersionWithVersioningStrict_WhenNoExplicitFiltersConfigured()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry =>
            {
                registry.AddOrchestrator<TestOrchestrator>();
                registry.AddActivity<TestActivity>();
            });
            builder.Configure(options =>
            {
                options.Versioning = new DurableTaskWorkerOptions.VersioningOptions
                {
                    Version = "1.0",
                    MatchStrategy = DurableTaskWorkerOptions.VersionMatchStrategy.Strict,
                };
            });
        });

        // Act
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();
        DurableTaskWorkerWorkItemFilters actual = filtersMonitor.Get("test");

        // Assert
        actual.Orchestrations.Should().ContainSingle(o => o.Name == nameof(TestOrchestrator) && o.Versions.Contains("1.0"));
        actual.Activities.Should().ContainSingle(a => a.Name == nameof(TestActivity) && a.Versions.Contains("1.0"));
    }

    [Fact]
    public void WorkItemFilters_DefaultEmptyRegistry_ProducesEmptyFilters()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(_ => { });
        });

        // Act
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
    public void WorkItemFilters_ExplicitFiltersOverrideDefaults()
    {
        // Arrange
        ServiceCollection services = new();
        DurableTaskWorkerWorkItemFilters customFilters = new()
        {
            Orchestrations = [new DurableTaskWorkerWorkItemFilters.OrchestrationFilter { Name = "CustomOrch", Versions = ["2.0"] }],
            Activities = [],
            Entities = [],
        };

        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry =>
            {
                registry.AddOrchestrator<TestOrchestrator>();
                registry.AddActivity<TestActivity>();
            });
            builder.UseWorkItemFilters(customFilters);
        });

        // Act
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();
        DurableTaskWorkerWorkItemFilters actual = filtersMonitor.Get("test");

        // Assert
        actual.Orchestrations.Should().ContainSingle(o => o.Name == "CustomOrch" && o.Versions.Contains("2.0"));
        actual.Activities.Should().BeEmpty();
        actual.Entities.Should().BeEmpty();
    }

    [Fact]
    public void WorkItemFilters_NullOverwritesDefaults()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry =>
            {
                registry.AddOrchestrator<TestOrchestrator>();
                registry.AddActivity<TestActivity>();
            });
            builder.UseWorkItemFilters(null);
        });

        // Act
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
    public void WorkItemFilters_EmptyFiltersOverrideDefaults()
    {
        // Arrange
        ServiceCollection services = new();
        DurableTaskWorkerWorkItemFilters emptyFilters = new()
        {
            Orchestrations = [],
            Activities = [],
            Entities = [],
        };

        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry =>
            {
                registry.AddOrchestrator<TestOrchestrator>();
                registry.AddActivity<TestActivity>();
            });
            builder.UseWorkItemFilters(emptyFilters);
        });

        // Act
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
    public void WorkItemFilters_NamedBuilders_HaveUniqueDefaultFilters()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddDurableTaskWorker("worker1", builder =>
        {
            builder.AddTasks(registry => registry.AddOrchestrator<TestOrchestrator>());
        });
        services.AddDurableTaskWorker("worker2", builder =>
        {
            builder.AddTasks(registry => registry.AddActivity<TestActivity>());
        });

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

        worker1Filters.Should().NotBeSameAs(worker2Filters);
    }

    sealed class TestOrchestrator : TaskOrchestrator<object, object>
    {
        public override Task<object> RunAsync(TaskOrchestrationContext context, object input)
        {
            throw new NotImplementedException();
        }
    }

    sealed class TestActivity : TaskActivity<object, object>
    {
        public override Task<object> RunAsync(TaskActivityContext context, object input)
        {
            throw new NotImplementedException();
        }
    }

    sealed class TestEntity : TaskEntity<object>
    {
    }
}