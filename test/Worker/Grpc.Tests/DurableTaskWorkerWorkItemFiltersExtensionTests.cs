// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Worker.Grpc.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

public class DurableTaskWorkerWorkItemFiltersExtensionTests
{
    [Fact]
    public void ToGrpcWorkItemFilters_EmptyFilters_ReturnsEmptyGrpcFilters()
    {
        // Arrange
        DurableTaskWorkerWorkItemFilters filters = new()
        {
            Orchestrations = [],
            Activities = [],
            Entities = [],
        };

        // Act
        P.WorkItemFilters result = filters.ToGrpcWorkItemFilters();

        // Assert
        result.Orchestrations.Should().BeEmpty();
        result.Activities.Should().BeEmpty();
        result.Entities.Should().BeEmpty();
    }

    [Fact]
    public void ToGrpcWorkItemFilters_WithOrchestration_ConvertsName()
    {
        // Arrange
        DurableTaskWorkerWorkItemFilters filters = new()
        {
            Orchestrations =
            [
                new DurableTaskWorkerWorkItemFilters.OrchestrationFilter
                {
                    Name = "TestOrchestrator",
                    Versions = [],
                },
            ],
            Activities = [],
            Entities = [],
        };

        // Act
        P.WorkItemFilters result = filters.ToGrpcWorkItemFilters();

        // Assert
        result.Orchestrations.Should().ContainSingle();
        result.Orchestrations[0].Name.Should().Be("TestOrchestrator");
        result.Orchestrations[0].Versions.Should().BeEmpty();
    }

    [Fact]
    public void ToGrpcWorkItemFilters_WithOrchestrationVersions_ConvertsVersions()
    {
        // Arrange
        DurableTaskWorkerWorkItemFilters filters = new()
        {
            Orchestrations =
            [
                new DurableTaskWorkerWorkItemFilters.OrchestrationFilter
                {
                    Name = "TestOrchestrator",
                    Versions = ["1.0", "2.0"],
                },
            ],
            Activities = [],
            Entities = [],
        };

        // Act
        P.WorkItemFilters result = filters.ToGrpcWorkItemFilters();

        // Assert
        result.Orchestrations.Should().ContainSingle();
        result.Orchestrations[0].Versions.Should().BeEquivalentTo(["1.0", "2.0"]);
    }

    [Fact]
    public void ToGrpcWorkItemFilters_WithActivity_ConvertsName()
    {
        // Arrange
        DurableTaskWorkerWorkItemFilters filters = new()
        {
            Orchestrations = [],
            Activities =
            [
                new DurableTaskWorkerWorkItemFilters.ActivityFilter
                {
                    Name = "TestActivity",
                    Versions = [],
                },
            ],
            Entities = [],
        };

        // Act
        P.WorkItemFilters result = filters.ToGrpcWorkItemFilters();

        // Assert
        result.Activities.Should().ContainSingle();
        result.Activities[0].Name.Should().Be("TestActivity");
        result.Activities[0].Versions.Should().BeEmpty();
    }

    [Fact]
    public void ToGrpcWorkItemFilters_WithActivityVersions_ConvertsVersions()
    {
        // Arrange
        DurableTaskWorkerWorkItemFilters filters = new()
        {
            Orchestrations = [],
            Activities =
            [
                new DurableTaskWorkerWorkItemFilters.ActivityFilter
                {
                    Name = "TestActivity",
                    Versions = ["v1", "v2", "v3"],
                },
            ],
            Entities = [],
        };

        // Act
        P.WorkItemFilters result = filters.ToGrpcWorkItemFilters();

        // Assert
        result.Activities.Should().ContainSingle();
        result.Activities[0].Versions.Should().BeEquivalentTo(["v1", "v2", "v3"]);
    }

    [Fact]
    public void ToGrpcWorkItemFilters_WithEntity_ConvertsName()
    {
        // Arrange
        DurableTaskWorkerWorkItemFilters filters = new()
        {
            Orchestrations = [],
            Activities = [],
            Entities =
            [
                new DurableTaskWorkerWorkItemFilters.EntityFilter
                {
                    Name = "testentity",
                },
            ],
        };

        // Act
        P.WorkItemFilters result = filters.ToGrpcWorkItemFilters();

        // Assert
        result.Entities.Should().ContainSingle();
        result.Entities[0].Name.Should().Be("testentity");
    }

    [Fact]
    public void ToGrpcWorkItemFilters_WithNullVersions_ConvertsWithoutError()
    {
        // Arrange
        DurableTaskWorkerWorkItemFilters filters = new()
        {
            Orchestrations =
            [
                new DurableTaskWorkerWorkItemFilters.OrchestrationFilter
                {
                    Name = "TestOrchestrator",
                },
            ],
            Activities =
            [
                new DurableTaskWorkerWorkItemFilters.ActivityFilter
                {
                    Name = "TestActivity",
                },
            ],
            Entities = [],
        };

        // Act
        P.WorkItemFilters result = filters.ToGrpcWorkItemFilters();

        // Assert
        result.Orchestrations.Should().ContainSingle();
        result.Orchestrations[0].Name.Should().Be("TestOrchestrator");
        result.Orchestrations[0].Versions.Should().BeEmpty();
        result.Activities.Should().ContainSingle();
        result.Activities[0].Name.Should().Be("TestActivity");
        result.Activities[0].Versions.Should().BeEmpty();
    }

    [Fact]
    public void ToGrpcWorkItemFilters_WithMultipleOrchestrations_ConvertsAll()
    {
        // Arrange
        DurableTaskWorkerWorkItemFilters filters = new()
        {
            Orchestrations =
            [
                new DurableTaskWorkerWorkItemFilters.OrchestrationFilter { Name = "Orch1", Versions = ["1.0"] },
                new DurableTaskWorkerWorkItemFilters.OrchestrationFilter { Name = "Orch2", Versions = ["2.0"] },
                new DurableTaskWorkerWorkItemFilters.OrchestrationFilter { Name = "Orch3", Versions = [] },
            ],
            Activities = [],
            Entities = [],
        };

        // Act
        P.WorkItemFilters result = filters.ToGrpcWorkItemFilters();

        // Assert
        result.Orchestrations.Should().HaveCount(3);
        result.Orchestrations.Select(o => o.Name).Should().BeEquivalentTo(["Orch1", "Orch2", "Orch3"]);
    }

    [Fact]
    public void ToGrpcWorkItemFilters_WithMultipleActivities_ConvertsAll()
    {
        // Arrange
        DurableTaskWorkerWorkItemFilters filters = new()
        {
            Orchestrations = [],
            Activities =
            [
                new DurableTaskWorkerWorkItemFilters.ActivityFilter { Name = "Activity1", Versions = [] },
                new DurableTaskWorkerWorkItemFilters.ActivityFilter { Name = "Activity2", Versions = [] },
            ],
            Entities = [],
        };

        // Act
        P.WorkItemFilters result = filters.ToGrpcWorkItemFilters();

        // Assert
        result.Activities.Should().HaveCount(2);
        result.Activities.Select(a => a.Name).Should().BeEquivalentTo(["Activity1", "Activity2"]);
    }

    [Fact]
    public void ToGrpcWorkItemFilters_WithMultipleEntities_ConvertsAll()
    {
        // Arrange
        DurableTaskWorkerWorkItemFilters filters = new()
        {
            Orchestrations = [],
            Activities = [],
            Entities =
            [
                new DurableTaskWorkerWorkItemFilters.EntityFilter { Name = "entity1" },
                new DurableTaskWorkerWorkItemFilters.EntityFilter { Name = "entity2" },
                new DurableTaskWorkerWorkItemFilters.EntityFilter { Name = "entity3" },
            ],
        };

        // Act
        P.WorkItemFilters result = filters.ToGrpcWorkItemFilters();

        // Assert
        result.Entities.Should().HaveCount(3);
        result.Entities.Select(e => e.Name).Should().BeEquivalentTo(["entity1", "entity2", "entity3"]);
    }

    [Fact]
    public void ToGrpcWorkItemFilters_WithMixedFilters_ConvertsAll()
    {
        // Arrange
        DurableTaskWorkerWorkItemFilters filters = new()
        {
            Orchestrations =
            [
                new DurableTaskWorkerWorkItemFilters.OrchestrationFilter { Name = "MyOrchestrator", Versions = ["1.0"] },
            ],
            Activities =
            [
                new DurableTaskWorkerWorkItemFilters.ActivityFilter { Name = "MyActivity", Versions = ["1.0", "2.0"] },
            ],
            Entities =
            [
                new DurableTaskWorkerWorkItemFilters.EntityFilter { Name = "myentity" },
            ],
        };

        // Act
        P.WorkItemFilters result = filters.ToGrpcWorkItemFilters();

        // Assert
        result.Orchestrations.Should().ContainSingle().Which.Name.Should().Be("MyOrchestrator");
        result.Orchestrations[0].Versions.Should().BeEquivalentTo(["1.0"]);
        result.Activities.Should().ContainSingle().Which.Name.Should().Be("MyActivity");
        result.Activities[0].Versions.Should().BeEquivalentTo(["1.0", "2.0"]);
        result.Entities.Should().ContainSingle().Which.Name.Should().Be("myentity");
    }

    [Fact]
    public void WorkerConstruction_DefaultFilters_FlowToWorker()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddSingleton<ILoggerFactory>(new Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory());

        services.AddDurableTaskWorker(builder =>
        {
            builder.UseGrpc();
            builder.AddTasks(registry =>
            {
                registry.AddOrchestrator<TestOrchestrator>();
                registry.AddActivity<TestActivity>();
            });
        });

        // Act
        using ServiceProvider provider = services.BuildServiceProvider();
        IHostedService hosted = Assert.Single(provider.GetServices<IHostedService>());
        Assert.IsType<GrpcDurableTaskWorker>(hosted);

        DurableTaskWorkerWorkItemFilters? filters = (DurableTaskWorkerWorkItemFilters?)typeof(GrpcDurableTaskWorker)
            .GetField("workItemFilters", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(hosted);

        // Assert
        filters.Should().NotBeNull();
        filters!.Orchestrations.Should().ContainSingle(o => o.Name == nameof(TestOrchestrator));
        filters.Activities.Should().ContainSingle(a => a.Name == nameof(TestActivity));
    }

    [Fact]
    public void WorkerConstruction_ExplicitFilters_FlowToWorker()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddSingleton<ILoggerFactory>(new Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory());

        DurableTaskWorkerWorkItemFilters customFilters = new()
        {
            Orchestrations = [new DurableTaskWorkerWorkItemFilters.OrchestrationFilter { Name = "CustomOrch", Versions = ["2.0"] }],
            Activities = [],
            Entities = [],
        };

        services.AddDurableTaskWorker(builder =>
        {
            builder.UseGrpc();
            builder.AddTasks(registry =>
            {
                registry.AddOrchestrator<TestOrchestrator>();
                registry.AddActivity<TestActivity>();
            });
            builder.UseWorkItemFilters(customFilters);
        });

        // Act
        using ServiceProvider provider = services.BuildServiceProvider();
        IHostedService hosted = Assert.Single(provider.GetServices<IHostedService>());
        Assert.IsType<GrpcDurableTaskWorker>(hosted);

        DurableTaskWorkerWorkItemFilters? filters = (DurableTaskWorkerWorkItemFilters?)typeof(GrpcDurableTaskWorker)
            .GetField("workItemFilters", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(hosted);

        // Assert
        filters.Should().NotBeNull();
        filters!.Orchestrations.Should().ContainSingle(o => o.Name == "CustomOrch" && o.Versions.Contains("2.0"));
        filters.Activities.Should().BeEmpty();
        filters.Entities.Should().BeEmpty();
    }

    [Fact]
    public void WorkerConstruction_NullFilters_ClearsDefaultsOnWorker()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddSingleton<ILoggerFactory>(new Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory());

        services.AddDurableTaskWorker(builder =>
        {
            builder.UseGrpc();
            builder.AddTasks(registry =>
            {
                registry.AddOrchestrator<TestOrchestrator>();
                registry.AddActivity<TestActivity>();
            });
            builder.UseWorkItemFilters(null);
        });

        // Act
        using ServiceProvider provider = services.BuildServiceProvider();
        IHostedService hosted = Assert.Single(provider.GetServices<IHostedService>());
        Assert.IsType<GrpcDurableTaskWorker>(hosted);

        DurableTaskWorkerWorkItemFilters? filters = (DurableTaskWorkerWorkItemFilters?)typeof(GrpcDurableTaskWorker)
            .GetField("workItemFilters", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(hosted);

        // Assert
        filters.Should().NotBeNull();
        filters!.Orchestrations.Should().BeEmpty();
        filters.Activities.Should().BeEmpty();
        filters.Entities.Should().BeEmpty();
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
}
