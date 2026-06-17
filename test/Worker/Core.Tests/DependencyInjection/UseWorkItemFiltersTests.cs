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
        builder.AddTasks(registry =>
        {
            registry.AddOrchestrator<TestOrchestrator>();
            registry.AddActivity<TestActivity>();
            registry.AddEntity<TestEntity>();
        });
        DurableTaskWorkerWorkItemFilters filters = new()
        {
            Orchestrations = [new DurableTaskWorkerWorkItemFilters.OrchestrationFilter { Name = nameof(TestOrchestrator), Versions = ["1.0"] }],
            Activities = [new DurableTaskWorkerWorkItemFilters.ActivityFilter { Name = nameof(TestActivity), Versions = [] }],
            Entities = [new DurableTaskWorkerWorkItemFilters.EntityFilter { Name = nameof(TestEntity) }],
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

        // Assert — unversioned-only registry produces an empty version list (filter wildcard) so the
        // backend can deliver versioned work items that the factory resolves via the documented
        // unversioned-fallback in DurableTaskFactory.TryCreateOrchestrator. Mixed and versioned-only
        // names emit concrete version sets instead.
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

        // Assert — unversioned-only registry emits the filter wildcard regardless of MatchStrategy
        // (so long as it's not Strict), so the backend can deliver versioned work items that the
        // factory resolves via the documented unversioned-fallback path.
        actual.Orchestrations.Should().ContainSingle(o => o.Name == nameof(TestOrchestrator) && o.Versions.Count == 0);
        actual.Activities.Should().ContainSingle(a => a.Name == nameof(TestActivity) && a.Versions.Count == 0);
    }

    [Fact]
    public void WorkItemFilters_DefaultVersionWithVersioningStrict_NarrowsGeneratedFilters_WhenExplicitlyOptedIn()
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
        actual.Orchestrations.Should().ContainSingle();
        actual.Orchestrations[0].Name.Should().Be(nameof(TestOrchestrator));
        actual.Orchestrations[0].Versions.Should().BeEquivalentTo(["1.0"]);
        actual.Activities.Should().ContainSingle();
        actual.Activities[0].Name.Should().Be(nameof(TestActivity));
        actual.Activities[0].Versions.Should().BeEquivalentTo(["1.0"]);
    }

    [Fact]
    public void WorkItemFilters_MixedRegistrationsWithVersioningStrict_OmitsNamesWithoutResolvableVersion()
    {
        // Arrange — name "FilterWorkflow" registers v="" + v="v2"; activity "FilterActivity" registers
        // v="" + v="v2". Worker is configured Strict + Version="1.0" with default (Implicit) fallback.
        // Under Implicit, the unversioned registration does NOT serve "1.0" because the name has
        // versioned siblings. The strict pre-dispatch gate would also reject any version != "1.0".
        // Therefore the worker cannot serve "1.0" for either name and both should be omitted from the
        // generated filter, so the backend does not deliver work items the worker will reject.
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry =>
            {
                registry.AddOrchestrator<UnversionedFilterWorkflow>();
                registry.AddOrchestrator<VersionedFilterWorkflowV2>();
                registry.AddActivity<UnversionedFilterActivity>();
                registry.AddActivity<VersionedFilterActivityV2>();
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
        actual.Orchestrations.Should().BeEmpty();
        actual.Activities.Should().BeEmpty();
    }

    [Fact]
    public void WorkItemFilters_MixedRegistrationsWithVersioningStrictMatchingRegisteredVersion_AdvertisesWorkerVersion()
    {
        // Arrange — same mixed registry as the previous test, but worker Version="v2" matches an exact
        // registration. Both name + (name, v2) resolve to the v2 implementation directly, so the filter
        // should advertise ["v2"] for each name.
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry =>
            {
                registry.AddOrchestrator<UnversionedFilterWorkflow>();
                registry.AddOrchestrator<VersionedFilterWorkflowV2>();
                registry.AddActivity<UnversionedFilterActivity>();
                registry.AddActivity<VersionedFilterActivityV2>();
            });
            builder.Configure(options =>
            {
                options.Versioning = new DurableTaskWorkerOptions.VersioningOptions
                {
                    Version = "v2",
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
        actual.Orchestrations.Should().ContainSingle();
        actual.Orchestrations[0].Name.Should().Be("FilterWorkflow");
        actual.Orchestrations[0].Versions.Should().BeEquivalentTo(["v2"]);
        actual.Activities.Should().ContainSingle();
        actual.Activities[0].Name.Should().Be("FilterActivity");
        actual.Activities[0].Versions.Should().BeEquivalentTo(["v2"]);
    }

    [Fact]
    public void WorkItemFilters_MixedRegistrationsWithVersioningStrictAndCatchAll_AdvertisesWorkerVersion()
    {
        // Arrange — same mixed registry; worker Version="1.0" (no exact match) but CatchAll is enabled
        // for both sides. The factory will resolve "1.0" via the unversioned registration on both
        // sides, so the filter must advertise ["1.0"] for each name (not omit them).
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry =>
            {
                registry.AddOrchestrator<UnversionedFilterWorkflow>();
                registry.AddOrchestrator<VersionedFilterWorkflowV2>();
                registry.AddActivity<UnversionedFilterActivity>();
                registry.AddActivity<VersionedFilterActivityV2>();
            });
            builder.Configure(options =>
            {
                options.Versioning = new DurableTaskWorkerOptions.VersioningOptions
                {
                    Version = "1.0",
                    MatchStrategy = DurableTaskWorkerOptions.VersionMatchStrategy.Strict,
                    OrchestratorUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.CatchAll,
                    ActivityUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.CatchAll,
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
        actual.Orchestrations.Should().ContainSingle();
        actual.Orchestrations[0].Versions.Should().BeEquivalentTo(["1.0"]);
        actual.Activities.Should().ContainSingle();
        actual.Activities[0].Versions.Should().BeEquivalentTo(["1.0"]);
    }

    [Fact]
    public void WorkItemFilters_StrictWithAsymmetricFallback_AdvertisesOrchestratorOmitsActivity()
    {
        // Arrange — strict + asymmetric per-side modes. Orchestrator side has CatchAll, so unmatched
        // versioned requests fall back to the unversioned orchestrator and the name is advertised.
        // Activity side stays at the default Implicit, where a mixed registry rejects fallback, so the
        // activity name must be omitted. This guards against accidentally using one side's mode for
        // the other in the strict path.
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry =>
            {
                registry.AddOrchestrator<UnversionedFilterWorkflow>();
                registry.AddOrchestrator<VersionedFilterWorkflowV2>();
                registry.AddActivity<UnversionedFilterActivity>();
                registry.AddActivity<VersionedFilterActivityV2>();
            });
            builder.Configure(options =>
            {
                options.Versioning = new DurableTaskWorkerOptions.VersioningOptions
                {
                    Version = "1.0",
                    MatchStrategy = DurableTaskWorkerOptions.VersionMatchStrategy.Strict,
                    OrchestratorUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.CatchAll,
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
        actual.Orchestrations.Should().ContainSingle();
        actual.Orchestrations[0].Name.Should().Be("FilterWorkflow");
        actual.Orchestrations[0].Versions.Should().BeEquivalentTo(["1.0"]);
        actual.Activities.Should().BeEmpty();
    }

    [Fact]
    public void WorkItemFilters_VersionedOnlyRegistrationsWithVersioningStrictAndStrictExactOnly_OmitsNames()
    {
        // Arrange — registry has only versioned registrations (v="v1" + v="v2") with no unversioned
        // entries. Worker is Strict + Version="v3" with StrictExactOnly. No exact match exists for "v3"
        // and StrictExactOnly disables all fallback. The names must be omitted.
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry =>
            {
                registry.AddOrchestrator<VersionedFilterWorkflowV1>();
                registry.AddOrchestrator<VersionedFilterWorkflowV2>();
                registry.AddActivity<VersionedFilterActivityV1>();
                registry.AddActivity<VersionedFilterActivityV2>();
            });
            builder.Configure(options =>
            {
                options.Versioning = new DurableTaskWorkerOptions.VersioningOptions
                {
                    Version = "v3",
                    MatchStrategy = DurableTaskWorkerOptions.VersionMatchStrategy.Strict,
                    OrchestratorUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.StrictExactOnly,
                    ActivityUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.StrictExactOnly,
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
        actual.Orchestrations.Should().BeEmpty();
        actual.Activities.Should().BeEmpty();
    }

    [Fact]
    public void WorkItemFilters_UnversionedOnlyRegistrationWithVersioningStrictAndStrictExactOnly_OmitsName()
    {
        // Arrange — name has only an unversioned registration. Worker is Strict + Version="1.0" with
        // StrictExactOnly. There is no exact match for "1.0" (only ""), and StrictExactOnly disables
        // the implicit fallback. The name must be omitted from the filter.
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry => registry.AddOrchestrator<UnversionedFilterWorkflow>());
            builder.Configure(options =>
            {
                options.Versioning = new DurableTaskWorkerOptions.VersioningOptions
                {
                    Version = "1.0",
                    MatchStrategy = DurableTaskWorkerOptions.VersionMatchStrategy.Strict,
                    OrchestratorUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.StrictExactOnly,
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
        actual.Orchestrations.Should().BeEmpty();
    }

    [Fact]
    public void WorkItemFilters_StrictWithEmptyWorkerVersion_NarrowsFilterToUnversioned()
    {
        // Arrange — Strict + Version="" means the worker only accepts unversioned work items. The filter
        // must narrow each name to [""] rather than emitting no version constraint (which would match all
        // versions and leave the worker to reject mismatches after the fact).
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry =>
            {
                registry.AddOrchestrator<UnversionedFilterWorkflow>();
                registry.AddActivity<UnversionedFilterActivity>();
            });
            builder.Configure(options =>
            {
                options.Versioning = new DurableTaskWorkerOptions.VersioningOptions
                {
                    Version = string.Empty,
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
        actual.Orchestrations.Should().ContainSingle();
        actual.Orchestrations[0].Name.Should().Be("FilterWorkflow");
        actual.Orchestrations[0].Versions.Should().BeEquivalentTo([string.Empty]);
        actual.Activities.Should().ContainSingle();
        actual.Activities[0].Name.Should().Be("FilterActivity");
        actual.Activities[0].Versions.Should().BeEquivalentTo([string.Empty]);
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
    public void WorkItemFilters_UnversionedAndVersionedOrchestrators_EmitConcreteVersionList()
    {
        // Arrange — register both an unversioned and a v2 orchestration under the same logical name.
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

        // Assert — emit ["", "v2"] (the literal registered set) instead of [] (match all). The factory's
        // dispatch rule refuses unversioned-fallback once any versioned registration exists, so emitting
        // [] here would cause the backend to stream unregistered versions the worker would then reject.
        actual.Orchestrations.Should().ContainSingle();
        actual.Orchestrations[0].Name.Should().Be("FilterWorkflow");
        actual.Orchestrations[0].Versions.Should().BeEquivalentTo([string.Empty, "v2"]);
    }

    [Fact]
    public void WorkItemFilters_UnversionedFallbackWithMixedOrchestrators_EmitsWildcardVersionList()
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
            builder.Configure(options =>
            {
                options.Versioning = new DurableTaskWorkerOptions.VersioningOptions
                {
                    OrchestratorUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.CatchAll,
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
        actual.Orchestrations.Should().ContainSingle();
        actual.Orchestrations[0].Name.Should().Be("FilterWorkflow");
        actual.Orchestrations[0].Versions.Should().BeEmpty();
    }

    [Fact]
    public void WorkItemFilters_OrchestratorFallbackOnly_DoesNotWidenActivityFilter()
    {
        // Arrange — orchestrator fallback ON, activity fallback OFF. Mixed orchestrator name widens to
        // wildcard; mixed activity name must still emit the concrete version list because the worker
        // refuses activity unversioned-fallback for that name.
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry =>
            {
                registry.AddOrchestrator<UnversionedFilterWorkflow>();
                registry.AddOrchestrator<VersionedFilterWorkflowV2>();
                registry.AddActivity<UnversionedFilterActivity>();
                registry.AddActivity<VersionedFilterActivityV2>();
            });
            builder.Configure(options =>
            {
                options.Versioning = new DurableTaskWorkerOptions.VersioningOptions
                {
                    OrchestratorUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.CatchAll,
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
        actual.Orchestrations.Should().ContainSingle();
        actual.Orchestrations[0].Versions.Should().BeEmpty();
        actual.Activities.Should().ContainSingle();
        actual.Activities[0].Versions.Should().BeEquivalentTo([string.Empty, "v2"]);
    }

    [Fact]
    public void WorkItemFilters_VersionedActivities_GroupVersionsByLogicalName()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry =>
            {
                registry.AddActivity<VersionedFilterActivityV1>();
                registry.AddActivity<VersionedFilterActivityV2>();
            });
            builder.UseWorkItemFilters();
        });

        // Act
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();
        DurableTaskWorkerWorkItemFilters actual = filtersMonitor.Get("test");

        // Assert
        actual.Activities.Should().ContainSingle();
        actual.Activities[0].Name.Should().Be("FilterActivity");
        actual.Activities[0].Versions.Should().BeEquivalentTo(["v1", "v2"]);
    }

    [Fact]
    public void WorkItemFilters_UnversionedAndVersionedActivities_EmitConcreteVersionList()
    {
        // Arrange — register both an unversioned and a v2 activity under the same logical name.
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry =>
            {
                registry.AddActivity<UnversionedFilterActivity>();
                registry.AddActivity<VersionedFilterActivityV2>();
            });
            builder.UseWorkItemFilters();
        });

        // Act
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();
        DurableTaskWorkerWorkItemFilters actual = filtersMonitor.Get("test");

        // Assert — emit ["", "v2"] (the literal registered set) instead of [] (match all). Same rationale
        // as the orchestrator-side test: dispatch refuses unversioned-fallback once any versioned
        // registration exists.
        actual.Activities.Should().ContainSingle();
        actual.Activities[0].Name.Should().Be("FilterActivity");
        actual.Activities[0].Versions.Should().BeEquivalentTo([string.Empty, "v2"]);
    }

    [Fact]
    public void WorkItemFilters_UnversionedFallbackWithMixedActivities_EmitsWildcardVersionList()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry =>
            {
                registry.AddActivity<UnversionedFilterActivity>();
                registry.AddActivity<VersionedFilterActivityV2>();
            });
            builder.Configure(options =>
            {
                options.Versioning = new DurableTaskWorkerOptions.VersioningOptions
                {
                    ActivityUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.CatchAll,
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
        actual.Activities.Should().ContainSingle();
        actual.Activities[0].Name.Should().Be("FilterActivity");
        actual.Activities[0].Versions.Should().BeEmpty();
    }

    [Fact]
    public void WorkItemFilters_StrictExactOnlyForOrchestrators_DoesNotWildcardUnversionedOnly()
    {
        // Arrange — only the unversioned orchestrator is registered. Under Implicit (default), the
        // filter would widen to wildcard [] because the factory resolves unmatched versions via the
        // implicit fallback. Under StrictExactOnly the factory rejects those requests, so the filter
        // MUST emit the concrete [""] version list to prevent the backend from delivering versioned
        // work items the worker will reject after the fact.
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry => registry.AddOrchestrator<UnversionedFilterWorkflow>());
            builder.Configure(options =>
            {
                options.Versioning = new DurableTaskWorkerOptions.VersioningOptions
                {
                    OrchestratorUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.StrictExactOnly,
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
        actual.Orchestrations.Should().ContainSingle();
        actual.Orchestrations[0].Name.Should().Be("FilterWorkflow");
        actual.Orchestrations[0].Versions.Should().BeEquivalentTo([string.Empty]);
    }

    [Fact]
    public void WorkItemFilters_StrictExactOnlyForActivities_DoesNotWildcardUnversionedOnly()
    {
        // Arrange — symmetric activity-side coverage for the StrictExactOnly filter behavior.
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry => registry.AddActivity<UnversionedFilterActivity>());
            builder.Configure(options =>
            {
                options.Versioning = new DurableTaskWorkerOptions.VersioningOptions
                {
                    ActivityUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.StrictExactOnly,
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
        actual.Activities.Should().ContainSingle();
        actual.Activities[0].Name.Should().Be("FilterActivity");
        actual.Activities[0].Versions.Should().BeEquivalentTo([string.Empty]);
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
        // Arrange - the explicit filter names must reference registered tasks, but the explicit
        // filters should still fully replace auto-generated ones (e.g., zero out Activities even
        // though TestActivity is registered).
        ServiceCollection services = new();
        DurableTaskWorkerWorkItemFilters customFilters = new()
        {
            Orchestrations = [new DurableTaskWorkerWorkItemFilters.OrchestrationFilter { Name = nameof(TestOrchestrator), Versions = ["2.0"] }],
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
        actual.Orchestrations.Should().ContainSingle(o => o.Name == nameof(TestOrchestrator) && o.Versions.Contains("2.0"));
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

    [Fact]
    public void WorkItemFilters_ExplicitOrchestrationFilterNotRegistered_ThrowsOnGet()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry => registry.AddOrchestrator<TestOrchestrator>());
            builder.UseWorkItemFilters(new DurableTaskWorkerWorkItemFilters
            {
                Orchestrations = [new DurableTaskWorkerWorkItemFilters.OrchestrationFilter { Name = "NotRegistered" }],
            });
        });

        // Act
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();
        Action act = () => filtersMonitor.Get("test");

        // Assert
        act.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().Contain(f => f.Contains("Orchestrations: [NotRegistered]"));
    }

    [Fact]
    public void WorkItemFilters_ExplicitActivityFilterNotRegistered_ThrowsOnGet()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry => registry.AddActivity<TestActivity>());
            builder.UseWorkItemFilters(new DurableTaskWorkerWorkItemFilters
            {
                Activities = [new DurableTaskWorkerWorkItemFilters.ActivityFilter { Name = "Ghost" }],
            });
        });

        // Act
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();
        Action act = () => filtersMonitor.Get("test");

        // Assert
        act.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().Contain(f => f.Contains("Activities: [Ghost]"));
    }

    [Fact]
    public void WorkItemFilters_ExplicitEntityFilterNotRegistered_ThrowsOnGet()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry => registry.AddEntity<TestEntity>());
            builder.UseWorkItemFilters(new DurableTaskWorkerWorkItemFilters
            {
                Entities = [new DurableTaskWorkerWorkItemFilters.EntityFilter { Name = "missing" }],
            });
        });

        // Act
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();
        Action act = () => filtersMonitor.Get("test");

        // Assert
        act.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().Contain(f => f.Contains("Entities: [missing]"));
    }

    [Fact]
    public void WorkItemFilters_MultipleUnknownNames_AreAllReported()
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
            builder.UseWorkItemFilters(new DurableTaskWorkerWorkItemFilters
            {
                Orchestrations =
                [
                    new DurableTaskWorkerWorkItemFilters.OrchestrationFilter { Name = nameof(TestOrchestrator) },
                    new DurableTaskWorkerWorkItemFilters.OrchestrationFilter { Name = "MissingOrch" },
                ],
                Activities =
                [
                    new DurableTaskWorkerWorkItemFilters.ActivityFilter { Name = "MissingActivity" },
                ],
                Entities =
                [
                    new DurableTaskWorkerWorkItemFilters.EntityFilter { Name = "missingEntity" },
                ],
            });
        });

        // Act
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();
        Action act = () => filtersMonitor.Get("test");

        // Assert
        act.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().ContainSingle()
            .Which.Should().Match(f =>
                ((string)f).Contains("Orchestrations: [MissingOrch]")
                && ((string)f).Contains("Activities: [MissingActivity]")
                && ((string)f).Contains("Entities: [missingEntity]"));
    }

    [Fact]
    public void WorkItemFilters_AllExplicitFiltersRegistered_DoesNotThrow()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry =>
            {
                registry.AddOrchestrator<TestOrchestrator>();
                registry.AddActivity<TestActivity>();
                registry.AddEntity<TestEntity>();
            });
            builder.UseWorkItemFilters(new DurableTaskWorkerWorkItemFilters
            {
                Orchestrations = [new DurableTaskWorkerWorkItemFilters.OrchestrationFilter { Name = nameof(TestOrchestrator) }],
                Activities = [new DurableTaskWorkerWorkItemFilters.ActivityFilter { Name = nameof(TestActivity) }],
                Entities = [new DurableTaskWorkerWorkItemFilters.EntityFilter { Name = nameof(TestEntity) }],
            });
        });

        // Act
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();
        Action act = () => filtersMonitor.Get("test");

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void WorkItemFilters_FilterNameDiffersInCase_DoesNotThrow()
    {
        // Arrange - TaskName equality is OrdinalIgnoreCase, so casing should not matter.
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry => registry.AddOrchestrator<TestOrchestrator>());
            builder.UseWorkItemFilters(new DurableTaskWorkerWorkItemFilters
            {
                Orchestrations = [new DurableTaskWorkerWorkItemFilters.OrchestrationFilter
                {
                    Name = nameof(TestOrchestrator).ToUpperInvariant(),
                }],
            });
        });

        // Act
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();
        Action act = () => filtersMonitor.Get("test");

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void WorkItemFilters_InvalidExplicitOverwrittenByAutoGen_DoesNotThrow()
    {
        // Arrange - the second UseWorkItemFilters() overwrites the invalid explicit filters with
        // auto-generated ones. The validator must run against the final state (auto-gen), not the
        // intermediate (invalid) state.
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry => registry.AddOrchestrator<TestOrchestrator>());
            builder.UseWorkItemFilters(new DurableTaskWorkerWorkItemFilters
            {
                Orchestrations = [new DurableTaskWorkerWorkItemFilters.OrchestrationFilter { Name = "WillBeOverwritten" }],
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
    }

    [Fact]
    public void WorkItemFilters_InvalidExplicitOverwrittenByNull_DoesNotThrow()
    {
        // Arrange - clearing filters with null must not throw based on the cleared, prior-invalid state.
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry => registry.AddOrchestrator<TestOrchestrator>());
            builder.UseWorkItemFilters(new DurableTaskWorkerWorkItemFilters
            {
                Orchestrations = [new DurableTaskWorkerWorkItemFilters.OrchestrationFilter { Name = "Bogus" }],
            });
            builder.UseWorkItemFilters(null);
        });

        // Act
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();
        DurableTaskWorkerWorkItemFilters actual = null!;
        Action act = () => actual = filtersMonitor.Get("test");

        // Assert
        act.Should().NotThrow();
        actual.Orchestrations.Should().BeEmpty();
        actual.Activities.Should().BeEmpty();
        actual.Entities.Should().BeEmpty();
    }

    [Fact]
    public void WorkItemFilters_InvalidExplicitOverwrittenByValidExplicit_DoesNotThrow()
    {
        // Arrange - the last UseWorkItemFilters call wins; validation must reflect the final state.
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry => registry.AddOrchestrator<TestOrchestrator>());
            builder.UseWorkItemFilters(new DurableTaskWorkerWorkItemFilters
            {
                Orchestrations = [new DurableTaskWorkerWorkItemFilters.OrchestrationFilter { Name = "Stale" }],
            });
            builder.UseWorkItemFilters(new DurableTaskWorkerWorkItemFilters
            {
                Orchestrations = [new DurableTaskWorkerWorkItemFilters.OrchestrationFilter { Name = nameof(TestOrchestrator) }],
            });
        });

        // Act
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();
        Action act = () => filtersMonitor.Get("test");

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void WorkItemFilters_NullExplicitFilters_DoNotRegisterValidator()
    {
        // Arrange - passing null is an explicit opt-out; no validator should be registered because
        // there is nothing to validate (and the worker should not pull DurableTaskRegistry into the
        // resolution chain when filtering is disabled).
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(_ => { });
            builder.UseWorkItemFilters(null);
        });

        // Assert - no validator registered.
        services.Should().NotContain(
            d => d.ServiceType == typeof(IValidateOptions<DurableTaskWorkerWorkItemFilters>),
            "passing null should not opt the worker into filter-name validation");
    }

    [Fact]
    public void WorkItemFilters_EmptyExplicitFilters_DoNotRegisterValidator()
    {
        // Arrange - an explicit but empty filter set has nothing to validate, so the validator
        // should not be registered.
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(_ => { });
            builder.UseWorkItemFilters(new DurableTaskWorkerWorkItemFilters());
        });

        // Assert
        services.Should().NotContain(
            d => d.ServiceType == typeof(IValidateOptions<DurableTaskWorkerWorkItemFilters>),
            "empty filters have nothing to validate");
    }

    [Fact]
    public void WorkItemFilters_DefaultNamedWorkerInvalidFilter_FailureMessageUsesDefaultPlaceholder()
    {
        // Arrange - default-name worker (Options.DefaultName == string.Empty) with an invalid filter.
        // The failure message should display "<default>" instead of an empty quoted name so the
        // misconfigured worker is identifiable.
        ServiceCollection services = new();
        services.AddDurableTaskWorker(builder =>
        {
            builder.AddTasks(registry => registry.AddOrchestrator<TestOrchestrator>());
            builder.UseWorkItemFilters(new DurableTaskWorkerWorkItemFilters
            {
                Orchestrations = [new DurableTaskWorkerWorkItemFilters.OrchestrationFilter { Name = "DoesNotExist" }],
            });
        });

        // Act
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();
        Action act = () => filtersMonitor.Get(Options.DefaultName);

        // Assert
        act.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().ContainSingle()
            .Which.Should().Contain("worker '<default>'");
    }

    [Fact]
    public void WorkItemFilters_RepeatedExplicitInvalidCalls_ReportFailureExactlyOnce()
    {
        // Arrange - even when the final state is invalid, the failure should be reported by a single
        // validator, not duplicated once per UseWorkItemFilters call.
        ServiceCollection services = new();
        services.AddDurableTaskWorker("test", builder =>
        {
            builder.AddTasks(registry => registry.AddOrchestrator<TestOrchestrator>());
            builder.UseWorkItemFilters(new DurableTaskWorkerWorkItemFilters
            {
                Orchestrations = [new DurableTaskWorkerWorkItemFilters.OrchestrationFilter { Name = "Bogus1" }],
            });
            builder.UseWorkItemFilters(new DurableTaskWorkerWorkItemFilters
            {
                Orchestrations = [new DurableTaskWorkerWorkItemFilters.OrchestrationFilter { Name = "Bogus2" }],
            });
        });

        // Act
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();
        Action act = () => filtersMonitor.Get("test");

        // Assert - one Failures entry, not two; the final state ("Bogus2") is what fails.
        act.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().ContainSingle()
            .Which.Should().Contain("Orchestrations: [Bogus2]");
    }

    [Fact]
    public void WorkItemFilters_ValidationIsScopedToEachNamedWorker()
    {
        // Arrange - worker A has invalid explicit filters; worker B is fine.
        // Getting B's options should not throw because of A's misconfiguration.
        ServiceCollection services = new();
        services.AddDurableTaskWorker("workerA", builder =>
        {
            builder.AddTasks(registry => registry.AddOrchestrator<TestOrchestrator>());
            builder.UseWorkItemFilters(new DurableTaskWorkerWorkItemFilters
            {
                Orchestrations = [new DurableTaskWorkerWorkItemFilters.OrchestrationFilter { Name = "Unknown" }],
            });
        });
        services.AddDurableTaskWorker("workerB", builder =>
        {
            builder.AddTasks(registry => registry.AddActivity<TestActivity>());
            builder.UseWorkItemFilters(new DurableTaskWorkerWorkItemFilters
            {
                Activities = [new DurableTaskWorkerWorkItemFilters.ActivityFilter { Name = nameof(TestActivity) }],
            });
        });

        // Act
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskWorkerWorkItemFilters> filtersMonitor =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>();

        Action getA = () => filtersMonitor.Get("workerA");
        Action getB = () => filtersMonitor.Get("workerB");

        // Assert
        getA.Should().Throw<OptionsValidationException>();
        getB.Should().NotThrow();
    }

    sealed class TestOrchestrator : TaskOrchestrator<object, object>
    {
        public override Task<object> RunAsync(TaskOrchestrationContext context, object input)
        {
            throw new NotImplementedException();
        }
    }

    [DurableTask("FilterWorkflow", Version = "v1")]
    sealed class VersionedFilterWorkflowV1 : TaskOrchestrator<string, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
        {
            return Task.FromResult("v1");
        }
    }

    [DurableTask("FilterWorkflow", Version = "v2")]
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

    [DurableTask("FilterActivity", Version = "v1")]
    sealed class VersionedFilterActivityV1 : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string input)
        {
            return Task.FromResult("v1");
        }
    }

    [DurableTask("FilterActivity", Version = "v2")]
    sealed class VersionedFilterActivityV2 : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string input)
        {
            return Task.FromResult("v2");
        }
    }

    [DurableTask("FilterActivity")]
    sealed class UnversionedFilterActivity : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string input)
        {
            return Task.FromResult("unversioned");
        }
    }

    sealed class TestEntity : TaskEntity<object>
    {
    }
}
