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

public class ServerlessActivitiesTests
{
    const string TaskHub = "testhub";

    [Fact]
    public async Task ServerlessActivityDeclarationHostedService_SendsDeclarationPayload()
    {
        // Arrange
        ServerlessOptions options = new()
        {
            TaskHub = TaskHub,
            WorkerProfileId = "profile-a",
            ContainerImage = "mcr.microsoft.com/durabletask/demo-worker:1.0",
            Cpu = "500m",
            Memory = "1024Mi",
            LaunchCommand = "cd /app && dotnet DemoWorker.dll",
            MaxConcurrentActivities = 7,
        };
        options.ActivityNames.Add("RemoteHello");
        options.EnvironmentVariables.Add("CUSTOM_SETTING", "enabled");
        options.Entrypoint.Add("/usr/bin/tini");
        options.Entrypoint.Add("--");
        options.Cmd.Add("dotnet");
        options.Cmd.Add("/app/DemoWorker.dll");
        FakeServerlessActivitiesClient client = new();
        ServerlessActivityDeclarationHostedService service = new(
            client,
            options,
            NullLogger<ServerlessActivityDeclarationHostedService>.Instance);

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        ServerlessActivityDeclaration declaration = client.Declarations.Should().ContainSingle().Subject;
        client.DeclarationTaskHubs.Should().Equal(TaskHub);
        declaration.WorkerProfileId.Should().Be("profile-a");
        declaration.ActivityNames.Should().Equal("RemoteHello");
        declaration.Image.ImageRef.Should().Be("mcr.microsoft.com/durabletask/demo-worker:1.0");
        declaration.Image.PublicPull.Should().BeTrue();
        declaration.Resources.Cpu.Should().Be("500m");
        declaration.Resources.Memory.Should().Be("1024Mi");
        declaration.EnvironmentVariables.Should().ContainKey("CUSTOM_SETTING").WhoseValue.Should().Be("enabled");
        declaration.Entrypoint.Should().Equal("/usr/bin/tini", "--");
        declaration.Cmd.Should().Equal("dotnet", "/app/DemoWorker.dll");
        declaration.LaunchCommand.Should().Be("cd /app && dotnet DemoWorker.dll");
        declaration.MaxConcurrentActivities.Should().Be(7);
    }

    [Fact]
    public async Task ServerlessActivityDeclarationHostedService_SkipsDeclarationWhenNamesAreEmpty()
    {
        // Arrange
        ServerlessOptions options = new()
        {
            TaskHub = TaskHub,
            ContainerImage = "example.com/repo/worker:latest",
        };
        FakeServerlessActivitiesClient client = new();
        ServerlessActivityDeclarationHostedService service = new(
            client,
            options,
            NullLogger<ServerlessActivityDeclarationHostedService>.Instance);

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        client.Declarations.Should().BeEmpty();
    }

    [Fact]
    public async Task ServerlessActivityDeclarationHostedService_RetriesTransientFailures()
    {
        // Arrange
        ServerlessOptions options = new()
        {
            TaskHub = TaskHub,
            ContainerImage = "example.com/repo/worker@sha256:abc",
            DeclarationRetryMaxAttempts = 2,
            DeclarationRetryDelay = TimeSpan.Zero,
        };
        options.ActivityNames.Add("RemoteHello");
        FakeServerlessActivitiesClient client = new() { TransientDeclarationFailures = 1 };
        ServerlessActivityDeclarationHostedService service = new(
            client,
            options,
            NullLogger<ServerlessActivityDeclarationHostedService>.Instance);

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        client.DeclarationAttempts.Should().Be(2);
        client.Declarations.Should().ContainSingle();
    }

    [Fact]
    public async Task ServerlessActivityDeclarationHostedService_RejectsPrivatePullImages()
    {
        // Arrange
        ServerlessOptions options = new()
        {
            TaskHub = TaskHub,
            ContainerImage = "example.com/repo/worker:latest",
            PublicPull = false,
        };
        options.ActivityNames.Add("RemoteHello");
        ServerlessActivityDeclarationHostedService service = new(
            new FakeServerlessActivitiesClient(),
            options,
            NullLogger<ServerlessActivityDeclarationHostedService>.Instance);

        // Act
        Func<Task> action = () => service.StartAsync(CancellationToken.None);

        // Assert
        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Serverless activity images must be publicly pullable for private preview.");
    }

    [Fact]
    public async Task ServerlessActivityWorkerRegistrationHostedService_SendsLiveWorkerMetadataWithoutActivityCatalog()
    {
        // Arrange
        string? originalSubstrate = Environment.GetEnvironmentVariable("DTS_SUBSTRATE");
        string? originalSandboxId = Environment.GetEnvironmentVariable("DTS_SANDBOX_ID");
        Environment.SetEnvironmentVariable("DTS_SUBSTRATE", "Sandbox");
        Environment.SetEnvironmentVariable("DTS_SANDBOX_ID", "sandbox-1");

        try
        {
            ServerlessOptions options = new()
            {
                Mode = ServerlessMode.ServerlessInclude,
                TaskHub = TaskHub,
                WorkerProfileId = "profile-a",
                MaxConcurrentActivities = 3,
                HeartbeatInterval = TimeSpan.FromDays(1),
            };
            options.ActivityNames.Add("RemoteHello");
            FakeServerlessActivitiesClient client = new();
            ServerlessActivityWorkerRegistrationHostedService service = new(
                client,
                options,
                NullLogger<ServerlessActivityWorkerRegistrationHostedService>.Instance);

            // Act
            await service.StartAsync(CancellationToken.None);
            await service.StopAsync(CancellationToken.None);

            // Assert
            client.SessionTaskHubs.Should().Equal(TaskHub);
            ServerlessActivityWorkerMessage message = client.Session.Messages.Should().ContainSingle().Subject;
            ServerlessActivityWorkerStart start = message.Start;
            start.TaskHub.Should().Be(TaskHub);
            start.WorkerProfileId.Should().Be("profile-a");
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
    public async Task UseServerlessActivities_LocalExclude_ConfiguresLocalWorkerExclusionFilter()
    {
        // Arrange
        using EnvironmentVariableScope serverlessActivities = new("DTS_SERVERLESS_ACTIVITIES", null);
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        // Act
        mockBuilder.Object.UseServerlessActivities(options =>
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
    public async Task UseServerlessActivities_DoesNotConfigureFilterWhenActivityNamesAreEmpty()
    {
        // Arrange
        using EnvironmentVariableScope serverlessActivities = new("DTS_SERVERLESS_ACTIVITIES", null);
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        // Act
        mockBuilder.Object.UseServerlessActivities(options =>
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
    public async Task UseServerlessActivities_ServerlessInclude_ConfiguresServerlessActivityWorkerFilter()
    {
        // Arrange
        using EnvironmentVariableScope serverlessActivities = new("DTS_SERVERLESS_ACTIVITIES", null);
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        // Act
        mockBuilder.Object.UseServerlessActivities(options =>
        {
            options.Mode = ServerlessMode.ServerlessInclude;
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

    sealed class FakeServerlessActivitiesClient : IServerlessActivitiesClient
    {
        public int TransientDeclarationFailures { get; init; }

        public int DeclarationAttempts { get; private set; }

        public List<ServerlessActivityDeclaration> Declarations { get; } = [];

        public List<string> DeclarationTaskHubs { get; } = [];

        public List<string> SessionTaskHubs { get; } = [];

        public FakeServerlessActivityWorkerSession Session { get; } = new();

        public Task<ServerlessActivityDeclarationResult> DeclareServerlessActivitiesAsync(
            ServerlessActivityDeclaration declaration,
            string taskHub,
            CancellationToken cancellationToken)
        {
            this.DeclarationAttempts++;
            if (this.DeclarationAttempts <= this.TransientDeclarationFailures)
            {
                throw new RpcException(new Status(StatusCode.Unavailable, "transient"));
            }

            this.DeclarationTaskHubs.Add(taskHub);
            this.Declarations.Add(declaration.Clone());
            return Task.FromResult(new ServerlessActivityDeclarationResult());
        }

        public IServerlessActivityWorkerSession OpenServerlessActivityWorkerSession(string taskHub, CancellationToken cancellationToken)
        {
            this.SessionTaskHubs.Add(taskHub);
            return this.Session;
        }
    }

    sealed class FakeServerlessActivityWorkerSession : IServerlessActivityWorkerSession
    {
        public List<ServerlessActivityWorkerMessage> Messages { get; } = [];

        public Task WriteMessageAsync(ServerlessActivityWorkerMessage message)
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