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
    public void WorkItemFilters_DefaultFromRegistry_WhenExplicitlyOptedIn()
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
            builder.UseWorkItemFilters();
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
    public void WorkItemFilters_DefaultWithEntity_WhenExplicitlyOptedIn()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry =>
            {
                registry.AddEntity<TestEntity>();
            });
            builder.UseWorkItemFilters();
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
    public void WorkItemFilters_DefaultNullWithVersioningCurrentOrOlder_WhenExplicitlyOptedIn()
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
            builder.UseWorkItemFilters();
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
    public void WorkItemFilters_DefaultNullWithVersioningNone_WhenExplicitlyOptedIn()
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
            builder.UseWorkItemFilters();
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
    public void WorkItemFilters_DefaultVersionWithVersioningStrict_AppliesToActivitiesOnly_WhenExplicitlyOptedIn()
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
            builder.UseWorkItemFilters();
        });

        // Act
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();
        DurableTaskWorkerWorkItemFilters actual = filtersMonitor.Get("test");

        // Assert
        actual.Orchestrations.Should().ContainSingle(o => o.Name == nameof(TestOrchestrator) && o.Versions.Count == 0);
        actual.Activities.Should().ContainSingle(a => a.Name == nameof(TestActivity) && a.Versions.Contains("1.0"));
    }

    [Fact]
    public void WorkItemFilters_VersionedOrchestrators_GroupVersionsByLogicalName()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry =>
            {
                registry.AddOrchestrator<VersionedFilterWorkflowV1>();
                registry.AddOrchestrator<VersionedFilterWorkflowV2>();
            });
            builder.UseWorkItemFilters();
        });

        // Act
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();
        DurableTaskWorkerWorkItemFilters actual = filtersMonitor.Get("test");

        // Assert
        actual.Orchestrations.Should().ContainSingle();
        actual.Orchestrations[0].Name.Should().Be("FilterWorkflow");
        actual.Orchestrations[0].Versions.Should().BeEquivalentTo(["v1", "v2"]);
    }

    [Fact]
    public void WorkItemFilters_UnversionedAndVersionedOrchestrators_FallBackToNameOnlyFilter()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry =>
            {
                registry.AddOrchestrator<UnversionedFilterWorkflow>();
                registry.AddOrchestrator<VersionedFilterWorkflowV2>();
            });
            builder.UseWorkItemFilters();
        });

        // Act
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();
        DurableTaskWorkerWorkItemFilters actual = filtersMonitor.Get("test");

        // Assert
        actual.Orchestrations.Should().ContainSingle();
        actual.Orchestrations[0].Name.Should().Be("FilterWorkflow");
        actual.Orchestrations[0].Versions.Should().BeEmpty();
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
    public void WorkItemFilters_DefaultNoFilters_WhenNoExplicitOptIn()
    {
        // Arrange - register tasks but do NOT call UseWorkItemFilters()
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry =>
            {
                registry.AddOrchestrator<TestOrchestrator>();
                registry.AddActivity<TestActivity>();
                registry.AddEntity<TestEntity>();
            });
        });

        // Act
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();
        DurableTaskWorkerWorkItemFilters actual = filtersMonitor.Get("test");

        // Assert - with no explicit opt-in, filters should be empty (legacy behavior)
        actual.Orchestrations.Should().BeEmpty();
        actual.Activities.Should().BeEmpty();
        actual.Entities.Should().BeEmpty();
    }

    [Fact]
    public void WorkItemFilters_ExplicitFiltersOverrideAutoGenerated()
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
    public void WorkItemFilters_NullClearsAutoGeneratedFilters()
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
            builder.UseWorkItemFilters();  // opt-in to auto-generated filters
            builder.UseWorkItemFilters(null); // then clear them
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
    public void WorkItemFilters_EmptyFiltersClearAutoGenerated()
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
            builder.UseWorkItemFilters(); // opt-in to auto-generated filters
            builder.UseWorkItemFilters(emptyFilters); // then clear them with empty
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
    public void WorkItemFilters_NamedBuilders_HaveUniqueDefaultFilters_WhenExplicitlyOptedIn()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddDurableTaskWorker("worker1", builder =>
        {
            builder.AddTasks(registry => registry.AddOrchestrator<TestOrchestrator>());
            builder.UseWorkItemFilters();
        });
        services.AddDurableTaskWorker("worker2", builder =>
        {
            builder.AddTasks(registry => registry.AddActivity<TestActivity>());
            builder.UseWorkItemFilters();
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

    [DurableTask("FilterWorkflow")]
    [DurableTaskVersion("v1")]
    sealed class VersionedFilterWorkflowV1 : TaskOrchestrator<string, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
        {
            return Task.FromResult("v1");
        }
    }

    [DurableTask("FilterWorkflow")]
    [DurableTaskVersion("v2")]
    sealed class VersionedFilterWorkflowV2 : TaskOrchestrator<string, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
        {
            return Task.FromResult("v2");
        }
    }

    [DurableTask("FilterWorkflow")]
    sealed class UnversionedFilterWorkflow : TaskOrchestrator<string, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
        {
            return Task.FromResult("unversioned");
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
