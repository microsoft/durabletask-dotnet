// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Grpc.Core;
using Microsoft.DurableTask.Protobuf.Serverless;
using Microsoft.DurableTask.Worker.AzureManaged.Serverless;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.DurableTask.Worker.AzureManaged.Tests;

public class RemoteActivitiesTests
{
    const string TaskHub = "testhub";

    [Fact]
    public async Task RemoteActivityDeclarationHostedService_SendsDeclarationPayload()
    {
        // Arrange
        RemoteActivityOptions options = new()
        {
            TaskHub = TaskHub,
            ContainerImage = "mcr.microsoft.com/durabletask/demo-worker:1.0",
            MaxConcurrentActivities = 7,
        };
        options.ActivityNames.Add("RemoteHello");
        options.EnvironmentVariables.Add("CUSTOM_SETTING", "enabled");
        FakeServerlessActivitiesClient client = new();
        RemoteActivityDeclarationHostedService service = new(
            client,
            options,
            NullLogger<RemoteActivityDeclarationHostedService>.Instance);

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        RemoteActivityDeclaration declaration = client.Declarations.Should().ContainSingle().Subject;
        declaration.TaskHub.Should().Be(TaskHub);
        declaration.ActivityNames.Should().Equal("RemoteHello");
        declaration.Image.ImageRef.Should().Be("mcr.microsoft.com/durabletask/demo-worker:1.0");
        declaration.Image.PublicPull.Should().BeTrue();
        declaration.EnvironmentVariables.Should().ContainKey("CUSTOM_SETTING").WhoseValue.Should().Be("enabled");
        declaration.MaxConcurrentActivities.Should().Be(7);
    }

    [Fact]
    public async Task RemoteActivityDeclarationHostedService_SkipsDeclarationWhenNamesAreEmpty()
    {
        // Arrange
        RemoteActivityOptions options = new()
        {
            TaskHub = TaskHub,
            ContainerImage = "example.com/repo/worker:latest",
        };
        FakeServerlessActivitiesClient client = new();
        RemoteActivityDeclarationHostedService service = new(
            client,
            options,
            NullLogger<RemoteActivityDeclarationHostedService>.Instance);

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        client.Declarations.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoteActivityDeclarationHostedService_RetriesTransientFailures()
    {
        // Arrange
        RemoteActivityOptions options = new()
        {
            TaskHub = TaskHub,
            ContainerImage = "example.com/repo/worker@sha256:abc",
            DeclarationRetryMaxAttempts = 2,
            DeclarationRetryDelay = TimeSpan.Zero,
        };
        options.ActivityNames.Add("RemoteHello");
        FakeServerlessActivitiesClient client = new() { TransientDeclarationFailures = 1 };
        RemoteActivityDeclarationHostedService service = new(
            client,
            options,
            NullLogger<RemoteActivityDeclarationHostedService>.Instance);

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        client.DeclarationAttempts.Should().Be(2);
        client.Declarations.Should().ContainSingle();
    }

    [Fact]
    public async Task RemoteActivityDeclarationHostedService_RejectsPrivatePullImages()
    {
        // Arrange
        RemoteActivityOptions options = new()
        {
            TaskHub = TaskHub,
            ContainerImage = "example.com/repo/worker:latest",
            PublicPull = false,
        };
        options.ActivityNames.Add("RemoteHello");
        RemoteActivityDeclarationHostedService service = new(
            new FakeServerlessActivitiesClient(),
            options,
            NullLogger<RemoteActivityDeclarationHostedService>.Instance);

        // Act
        Func<Task> action = () => service.StartAsync(CancellationToken.None);

        // Assert
        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Remote activity images must be publicly pullable for private preview.");
    }

    [Fact]
    public async Task RemoteActivityWorkerRegistrationHostedService_SendsLiveWorkerMetadataWithoutActivityCatalog()
    {
        // Arrange
        string? originalSubstrate = Environment.GetEnvironmentVariable("DTS_SUBSTRATE");
        string? originalSandboxId = Environment.GetEnvironmentVariable("DTS_SANDBOX_ID");
        Environment.SetEnvironmentVariable("DTS_SUBSTRATE", "Sandbox");
        Environment.SetEnvironmentVariable("DTS_SANDBOX_ID", "sandbox-1");

        try
        {
            RemoteActivityWorkerOptions options = new()
            {
                TaskHub = TaskHub,
                MaxConcurrentActivities = 3,
                HeartbeatInterval = TimeSpan.FromDays(1),
            };
            options.ActivityNames.Add("RemoteHello");
            FakeServerlessActivitiesClient client = new();
            RemoteActivityWorkerRegistrationHostedService service = new(
                client,
                options,
                NullLogger<RemoteActivityWorkerRegistrationHostedService>.Instance);

            // Act
            await service.StartAsync(CancellationToken.None);
            await service.StopAsync(CancellationToken.None);

            // Assert
            RemoteActivityWorkerMessage message = client.Session.Messages.Should().ContainSingle().Subject;
            RemoteActivityWorkerStart start = message.Start;
            start.TaskHub.Should().Be(TaskHub);
            start.MaxActivitiesCount.Should().Be(3);
            start.Substrate.Should().Be(SubstrateKind.Sandbox);
            start.SandboxId.Should().Be("sandbox-1");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DTS_SUBSTRATE", originalSubstrate);
            Environment.SetEnvironmentVariable("DTS_SANDBOX_ID", originalSandboxId);
        }
    }

    [Fact]
    public async Task DeclareRemoteActivities_ConfiguresLocalWorkerExclusionFilter()
    {
        // Arrange
        using EnvironmentVariableScope remoteActivities = new("DTS_REMOTE_ACTIVITIES", null);
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(b => b.Services).Returns(services);
        mockBuilder.Setup(b => b.Name).Returns(Options.DefaultName);

        // Act
        mockBuilder.Object.DeclareRemoteActivities(options =>
        {
            options.TaskHub = TaskHub;
            options.ContainerImage = "example.com/repo/worker:latest";
            options.ActivityNames.Add("RemoteHello");
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        DurableTaskWorkerWorkItemFilters filters = provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>().Get(Options.DefaultName);

        // Assert
        filters.ExcludedActivities.Select(filter => filter.Name).Should().Equal("RemoteHello");
        filters.Activities.Should().BeEmpty();
    }

    [Fact]
    public async Task DeclareRemoteActivities_DoesNotConfigureFilterWhenActivityNamesAreEmpty()
    {
        // Arrange
        using EnvironmentVariableScope remoteActivities = new("DTS_REMOTE_ACTIVITIES", null);
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        // Act
        mockBuilder.Object.DeclareRemoteActivities(options =>
        {
            options.TaskHub = TaskHub;
            options.ContainerImage = "example.com/repo/worker:latest";
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        DurableTaskWorkerWorkItemFilters filters = provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>().Get(Options.DefaultName);

        // Assert
        filters.ExcludedActivities.Should().BeEmpty();
        filters.Activities.Should().BeEmpty();
    }

    [Fact]
    public async Task UseRemoteActivityWorker_ConfiguresRemoteActivityWorkerFilter()
    {
        // Arrange
        using EnvironmentVariableScope remoteActivities = new("DTS_REMOTE_ACTIVITIES", null);
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(b => b.Services).Returns(services);
        mockBuilder.Setup(b => b.Name).Returns(Options.DefaultName);

        // Act
        mockBuilder.Object.UseRemoteActivityWorker(options =>
        {
            options.TaskHub = TaskHub;
            options.ActivityNames.Add("RemoteHello");
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        DurableTaskWorkerWorkItemFilters filters = provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>().Get(Options.DefaultName);

        // Assert
        filters.Activities.Select(filter => filter.Name).Should().Equal("RemoteHello");
        filters.ExcludedActivities.Should().BeEmpty();
        filters.Orchestrations.Should().BeEmpty();
        filters.Entities.Should().BeEmpty();
    }

    [Fact]
    public async Task UseRemoteActivityWorker_DoesNotConfigureFilterWhenActivityNamesAreEmpty()
    {
        // Arrange
        using EnvironmentVariableScope remoteActivities = new("DTS_REMOTE_ACTIVITIES", null);
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        // Act
        mockBuilder.Object.UseRemoteActivityWorker(options =>
        {
            options.TaskHub = TaskHub;
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        DurableTaskWorkerWorkItemFilters filters = provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>().Get(Options.DefaultName);

        // Assert
        filters.Activities.Should().BeEmpty();
        filters.ExcludedActivities.Should().BeEmpty();
    }

    sealed class FakeServerlessActivitiesClient : IServerlessActivitiesClient
    {
        public int TransientDeclarationFailures { get; init; }

        public int DeclarationAttempts { get; private set; }

        public List<RemoteActivityDeclaration> Declarations { get; } = [];

        public FakeRemoteActivityWorkerSession Session { get; } = new();

        public Task<RemoteActivityDeclarationResult> DeclareRemoteActivitiesAsync(
            RemoteActivityDeclaration declaration,
            CancellationToken cancellationToken)
        {
            this.DeclarationAttempts++;
            if (this.DeclarationAttempts <= this.TransientDeclarationFailures)
            {
                throw new RpcException(new Status(StatusCode.Unavailable, "transient"));
            }

            this.Declarations.Add(declaration.Clone());
            return Task.FromResult(new RemoteActivityDeclarationResult());
        }

        public IRemoteActivityWorkerSession OpenRemoteActivityWorkerSession(CancellationToken cancellationToken) => this.Session;
    }

    sealed class FakeRemoteActivityWorkerSession : IRemoteActivityWorkerSession
    {
        public List<RemoteActivityWorkerMessage> Messages { get; } = [];

        public Task WriteMessageAsync(RemoteActivityWorkerMessage message)
        {
            this.Messages.Add(message.Clone());
            return Task.CompletedTask;
        }

        public Task CompleteAsync() => Task.CompletedTask;

        public ValueTask DisposeAsync() => default;
    }

    sealed class EnvironmentVariableScope : IDisposable
    {
        readonly string name;
        readonly string? originalValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            this.name = name;
            this.originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(this.name, this.originalValue);
    }
}