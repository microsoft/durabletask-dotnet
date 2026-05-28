// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using FluentAssertions;
using Grpc.Core;
using Microsoft.DurableTask.Protobuf.Serverless;
using Microsoft.DurableTask.Worker.AzureManaged;
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
    public void ServerlessDeclarationContract_DoesNotExposeRemovedOptions()
    {
        typeof(ServerlessOptions).GetProperty("LaunchCommand").Should().BeNull();
        typeof(ServerlessOptions).GetProperty("DeclarationRetryMaxAttempts").Should().BeNull();
        typeof(ServerlessOptions).GetProperty("DeclarationRetryDelay").Should().BeNull();
        typeof(ServerlessOptions).GetProperty(
            "HeartbeatInterval",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Should().BeNull();
        typeof(ServerlessOptions).GetProperty("WakeupPort").Should().BeNull();
        typeof(ServerlessOptions).GetProperty(
            "WorkerRegistrationRetryInitialDelay",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Should().BeNull();
        typeof(ServerlessOptions).GetProperty(
            "WorkerRegistrationRetryMaxDelay",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Should().BeNull();
        typeof(ServerlessOptions).GetProperty(
            "Mode",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Should().BeNull();
        typeof(ServerlessActivityDeclaration).GetProperty("LaunchCommand").Should().BeNull();
    }

    [Fact]
    public void ServerlessDeclarationContract_ExposesProfileAddActivityOnly()
    {
        // Arrange
        Type optionsType = typeof(ServerlessOptions);
        Type? activityAttributeType = typeof(ServerlessOptions).Assembly.GetType(
            "Microsoft.DurableTask.Worker.AzureManaged.Serverless.ServerlessActivityAttribute");

        // Act/Assert
        optionsType.GetProperty("ActivityNames").Should().BeNull();
        optionsType.GetMethod("AddActivity", [typeof(string)]).Should().NotBeNull();
        optionsType.GetMethods().Should().Contain(method =>
            method.Name == "AddActivity" && method.IsGenericMethodDefinition);
        activityAttributeType.Should().BeNull();
    }

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
            MaxConcurrentActivities = 7,
        };
        options.AddActivity("RemoteHello");
        options.EnvironmentVariables.Add("CUSTOM_SETTING", "enabled");
        options.Entrypoint.Add("/usr/bin/tini");
        options.Entrypoint.Add("--");
        options.Cmd.Add("dotnet");
        options.Cmd.Add("/app/DemoWorker.dll");
        FakeServerlessActivitiesClient client = new();
        ServerlessActivityDeclarationHostedService service = new(
            client,
            options,
            runtimeOptions: null,
            NullLogger<ServerlessActivityDeclarationHostedService>.Instance);

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        ServerlessActivityDeclaration declaration = client.Declarations.Should().ContainSingle().Subject;
        client.DeclarationTaskHubs.Should().Equal(TaskHub);
        declaration.WorkerProfileId.Should().Be("profile-a");
        declaration.ActivityNames.Should().Equal("RemoteHello");
        declaration.Image.ImageRef.Should().Be("mcr.microsoft.com/durabletask/demo-worker:1.0");
        declaration.Resources.Cpu.Should().Be("500m");
        declaration.Resources.Memory.Should().Be("1024Mi");
        declaration.EnvironmentVariables.Should().ContainKey("CUSTOM_SETTING").WhoseValue.Should().Be("enabled");
        declaration.Entrypoint.Should().Equal("/usr/bin/tini", "--");
        declaration.Cmd.Should().Equal("dotnet", "/app/DemoWorker.dll");
        declaration.MaxConcurrentActivities.Should().Be(7);
    }

    [Fact]
    public async Task ServerlessActivitiesClientAdapter_SendsTaskHubMetadata()
    {
        // Arrange
        RecordingServerlessActivitiesCallInvoker callInvoker = new();
        ServerlessActivitiesClientAdapter adapter = new(new ServerlessActivities.ServerlessActivitiesClient(callInvoker));
        ServerlessActivityDeclaration declaration = new()
        {
            WorkerProfileId = "profile-a",
            Image = new ServerlessActivityImage
            {
                ImageRef = "example.com/repo/worker:latest",
            },
            Resources = new ServerlessActivityResources
            {
                Cpu = "500m",
                Memory = "1024Mi",
            },
            MaxConcurrentActivities = 7,
        };
        declaration.ActivityNames.Add("RemoteHello");

        // Act
        await adapter.DeclareServerlessActivitiesAsync(declaration, TaskHub, CancellationToken.None);
        await using IServerlessActivityWorkerSession session = adapter.OpenServerlessActivityWorkerSession(
            TaskHub,
            CancellationToken.None);

        // Assert
        callInvoker.DeclarationHeaders.Should().Contain(header => header.Key == "taskhub" && header.Value == TaskHub);
        callInvoker.WorkerSessionHeaders.Should().Contain(header => header.Key == "taskhub" && header.Value == TaskHub);
    }

    [Fact]
    public async Task ServerlessActivitiesClientAdapter_CanRelyOnChannelTaskHubMetadata()
    {
        // Arrange
        RecordingServerlessActivitiesCallInvoker callInvoker = new();
        ServerlessActivitiesClientAdapter adapter = new(
            new ServerlessActivities.ServerlessActivitiesClient(callInvoker),
            attachTaskHubMetadata: false);
        ServerlessActivityDeclaration declaration = new()
        {
            WorkerProfileId = "profile-a",
            Image = new ServerlessActivityImage
            {
                ImageRef = "example.com/repo/worker:latest",
            },
            Resources = new ServerlessActivityResources
            {
                Cpu = "500m",
                Memory = "1024Mi",
            },
            MaxConcurrentActivities = 7,
        };
        declaration.ActivityNames.Add("RemoteHello");

        // Act
        await adapter.DeclareServerlessActivitiesAsync(declaration, TaskHub, CancellationToken.None);
        await using IServerlessActivityWorkerSession session = adapter.OpenServerlessActivityWorkerSession(
            TaskHub,
            CancellationToken.None);

        // Assert
        callInvoker.DeclarationHeaders.Should().NotContain(header => header.Key == "taskhub");
        callInvoker.WorkerSessionHeaders.Should().NotContain(header => header.Key == "taskhub");
    }

    [Fact]
    public async Task ServerlessActivityDeclarationHostedService_OmitsEntrypointAndCmdByDefault()
    {
        // Arrange
        ServerlessOptions options = new()
        {
            TaskHub = TaskHub,
            ContainerImage = "mcr.microsoft.com/durabletask/demo-worker:1.0",
        };
        options.AddActivity("RemoteHello");
        FakeServerlessActivitiesClient client = new();
        ServerlessActivityDeclarationHostedService service = new(
            client,
            options,
            runtimeOptions: null,
            NullLogger<ServerlessActivityDeclarationHostedService>.Instance);

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        ServerlessActivityDeclaration declaration = client.Declarations.Should().ContainSingle().Subject;
        declaration.Entrypoint.Should().BeEmpty();
        declaration.Cmd.Should().BeEmpty();
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
            runtimeOptions: null,
            NullLogger<ServerlessActivityDeclarationHostedService>.Instance);

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        client.Declarations.Should().BeEmpty();
    }

    [Fact]
    public async Task ServerlessActivityDeclarationHostedService_DoesNotRetryTransientFailures()
    {
        // Arrange
        ServerlessOptions options = new()
        {
            TaskHub = TaskHub,
            ContainerImage = "example.com/repo/worker@sha256:abc",
        };
        options.AddActivity("RemoteHello");
        FakeServerlessActivitiesClient client = new() { TransientDeclarationFailures = 1 };
        ServerlessActivityDeclarationHostedService service = new(
            client,
            options,
            runtimeOptions: null,
            NullLogger<ServerlessActivityDeclarationHostedService>.Instance);

        // Act
        Func<Task> action = () => service.StartAsync(CancellationToken.None);

        // Assert
        await action.Should().ThrowAsync<RpcException>()
            .Where(exception => exception.StatusCode == StatusCode.Unavailable);
        client.DeclarationAttempts.Should().Be(1);
        client.Declarations.Should().BeEmpty();
    }

    [Fact]
    public async Task ServerlessActivityWorkerRegistrationHostedService_SendsLiveWorkerMetadataWithRegisteredActivities()
    {
        // Arrange
        string? originalSubstrate = Environment.GetEnvironmentVariable("DTS_SUBSTRATE");
        string? originalSandboxId = Environment.GetEnvironmentVariable("DTS_SANDBOX_ID");
        Environment.SetEnvironmentVariable("DTS_SUBSTRATE", "Sandbox");
        Environment.SetEnvironmentVariable("DTS_SANDBOX_ID", "sandbox-1");

        try
        {
            ServerlessWorkerRuntimeOptions options = new()
            {
                Mode = ServerlessMode.ServerlessInclude,
                TaskHub = TaskHub,
                WorkerProfileId = "profile-a",
                MaxConcurrentActivities = 3,
                HeartbeatInterval = TimeSpan.FromDays(1),
            };
            FakeServerlessActivitiesClient client = new();
            ServerlessActivityWorkerRegistrationHostedService service = new(
                client,
                options,
                ["RemoteHello"],
                NullLogger<ServerlessActivityWorkerRegistrationHostedService>.Instance);

            // Act
            await service.StartAsync(CancellationToken.None);
            await client.Session.WaitForMessageAsync(message => message.Start != null);
            await service.StopAsync(CancellationToken.None);

            // Assert
            client.SessionTaskHubs.Should().Equal(TaskHub);
            ServerlessActivityWorkerMessage message = client.Session.Messages.Should().ContainSingle().Subject;
            ServerlessActivityWorkerStart start = message.Start;
            start.TaskHub.Should().Be(TaskHub);
            start.WorkerProfileId.Should().Be("profile-a");
            start.MaxActivitiesCount.Should().Be(3);
            start.Substrate.Should().Be(SubstrateKind.Sandbox);
            start.DtsSandboxIdentifier.Should().Be("sandbox-1");
            start.ActivityNames.Should().Equal("RemoteHello");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DTS_SUBSTRATE", originalSubstrate);
            Environment.SetEnvironmentVariable("DTS_SANDBOX_ID", originalSandboxId);
        }
    }

    [Fact]
    public void ServerlessActivityTracker_TracksInFlightActivityCount()
    {
        // Arrange
        ServerlessActivityTracker activityTracker = new();

        // Act
        activityTracker.NotifyActivityStarted();
        activityTracker.NotifyActivityStarted();

        // Assert
        activityTracker.InFlightCount.Should().Be(2);

        // Act
        activityTracker.NotifyActivityCompleted();

        // Assert
        activityTracker.InFlightCount.Should().Be(1);

        // Act
        activityTracker.NotifyActivityCompleted();
        activityTracker.NotifyActivityCompleted();

        // Assert
        activityTracker.InFlightCount.Should().Be(0);
    }

    [Fact]
    public async Task ServerlessActivityWorkerRegistrationHostedService_SendsHeartbeatWithCurrentInFlightCount()
    {
        // Arrange
        ServerlessWorkerRuntimeOptions options = new()
        {
            Mode = ServerlessMode.ServerlessInclude,
            TaskHub = TaskHub,
            WorkerProfileId = "profile-a",
            MaxConcurrentActivities = 3,
            HeartbeatInterval = TimeSpan.FromMilliseconds(10),
        };

        FakeServerlessActivitiesClient client = new();
        ServerlessActivityTracker activityTracker = new();
        activityTracker.NotifyActivityStarted();
        activityTracker.NotifyActivityStarted();

        ServerlessActivityWorkerRegistrationHostedService service = new(
            client,
            options,
            ["RemoteHello"],
            NullLogger<ServerlessActivityWorkerRegistrationHostedService>.Instance,
            activityTracker: activityTracker);

        // Act
        await service.StartAsync(CancellationToken.None);
        await client.Session.WaitForMessageAsync(message => message.Heartbeat?.ActiveActivitiesCount == 2);
        activityTracker.NotifyActivityCompleted();
        await client.Session.WaitForMessageAsync(message => message.Heartbeat?.ActiveActivitiesCount == 1);
        await service.StopAsync(CancellationToken.None);

        // Assert
        client.Session.Messages.Should().Contain(message => message.Heartbeat != null && message.Heartbeat.ActiveActivitiesCount == 2);
        client.Session.Messages.Should().Contain(message => message.Heartbeat != null && message.Heartbeat.ActiveActivitiesCount == 1);
    }

    [Fact]
    public async Task ServerlessActivityWorkerRegistrationHostedService_ReopensSessionAfterTransientStreamFailure()
    {
        // Arrange
        ServerlessWorkerRuntimeOptions options = new()
        {
            Mode = ServerlessMode.ServerlessInclude,
            TaskHub = TaskHub,
            WorkerProfileId = "profile-a",
            MaxConcurrentActivities = 3,
            HeartbeatInterval = TimeSpan.FromMilliseconds(10),
            WorkerRegistrationRetryInitialDelay = TimeSpan.FromMilliseconds(10),
            WorkerRegistrationRetryMaxDelay = TimeSpan.FromMilliseconds(10),
        };

        FakeServerlessActivityWorkerSession failedSession = new() { ThrowOnWriteAttempt = 2 };
        FakeServerlessActivityWorkerSession recoveredSession = new();
        FakeServerlessActivitiesClient client = new();
        client.QueueSession(failedSession);
        client.QueueSession(recoveredSession);

        ServerlessActivityWorkerRegistrationHostedService service = new(
            client,
            options,
            ["RemoteHello"],
            NullLogger<ServerlessActivityWorkerRegistrationHostedService>.Instance);

        // Act
        await service.StartAsync(CancellationToken.None);
        await failedSession.WaitForMessageAsync(message => message.Start != null);
        await recoveredSession.WaitForMessageAsync(message => message.Start != null);
        await service.StopAsync(CancellationToken.None);

        // Assert
        client.SessionTaskHubs.Should().Equal(TaskHub, TaskHub);
        failedSession.Messages.Should().ContainSingle(message => message.Start != null);
        recoveredSession.Messages.Should().ContainSingle(message => message.Start != null);
    }

    [Fact]
    public async Task ServerlessActivityWorkerRegistrationHostedService_ReopensSessionAfterTerminalServerFailure()
    {
        // Arrange
        ServerlessWorkerRuntimeOptions options = new()
        {
            Mode = ServerlessMode.ServerlessInclude,
            TaskHub = TaskHub,
            WorkerProfileId = "profile-a",
            MaxConcurrentActivities = 3,
            HeartbeatInterval = TimeSpan.FromDays(1),
            WorkerRegistrationRetryInitialDelay = TimeSpan.FromMilliseconds(10),
            WorkerRegistrationRetryMaxDelay = TimeSpan.FromMilliseconds(10),
        };

        FakeServerlessActivityWorkerSession failedSession = new();
        FakeServerlessActivityWorkerSession recoveredSession = new();
        FakeServerlessActivitiesClient client = new();
        client.QueueSession(failedSession);
        client.QueueSession(recoveredSession);

        ServerlessActivityWorkerRegistrationHostedService service = new(
            client,
            options,
            ["RemoteHello"],
            NullLogger<ServerlessActivityWorkerRegistrationHostedService>.Instance);

        // Act
        await service.StartAsync(CancellationToken.None);
        await failedSession.WaitForMessageAsync(message => message.Start != null);
        failedSession.FailCompletion(new RpcException(new Status(StatusCode.Unavailable, "terminal")));
        await recoveredSession.WaitForMessageAsync(message => message.Start != null);
        await service.StopAsync(CancellationToken.None);

        // Assert
        client.SessionTaskHubs.Should().Equal(TaskHub, TaskHub);
        failedSession.Messages.Should().ContainSingle(message => message.Start != null);
        recoveredSession.Messages.Should().ContainSingle(message => message.Start != null);
    }

    [Fact]
    public void ServerlessActivityWorkerRegistrationHostedService_ComputeJitteredReconnectDelay_UsesFullJitterWindow()
    {
        // Arrange
        TimeSpan retryDelay = TimeSpan.FromSeconds(10);

        // Act
        TimeSpan zero = ServerlessActivityWorkerRegistrationHostedService.ComputeJitteredReconnectDelay(
            TimeSpan.Zero,
            new DeterministicRandom(0.5));
        TimeSpan low = ServerlessActivityWorkerRegistrationHostedService.ComputeJitteredReconnectDelay(
            retryDelay,
            new DeterministicRandom(0.0));
        TimeSpan mid = ServerlessActivityWorkerRegistrationHostedService.ComputeJitteredReconnectDelay(
            retryDelay,
            new DeterministicRandom(0.5));
        TimeSpan high = ServerlessActivityWorkerRegistrationHostedService.ComputeJitteredReconnectDelay(
            retryDelay,
            new DeterministicRandom(0.999999));

        // Assert
        zero.Should().Be(TimeSpan.Zero);
        low.Should().Be(TimeSpan.Zero);
        mid.Should().Be(TimeSpan.FromSeconds(5));
        high.Should().BeGreaterThan(TimeSpan.FromSeconds(9));
        high.Should().BeLessThan(retryDelay);
    }

    [Fact]
    public async Task ServerlessActivityWorkerRegistrationHostedService_AppliesJitterToReconnectDelay()
    {
        // Arrange
        ServerlessWorkerRuntimeOptions options = new()
        {
            Mode = ServerlessMode.ServerlessInclude,
            TaskHub = TaskHub,
            WorkerProfileId = "profile-a",
            MaxConcurrentActivities = 3,
            HeartbeatInterval = TimeSpan.FromMilliseconds(10),
            WorkerRegistrationRetryInitialDelay = TimeSpan.FromDays(1),
            WorkerRegistrationRetryMaxDelay = TimeSpan.FromDays(1),
        };

        FakeServerlessActivityWorkerSession failedSession = new() { ThrowOnWriteAttempt = 2 };
        FakeServerlessActivityWorkerSession recoveredSession = new();
        FakeServerlessActivitiesClient client = new();
        client.QueueSession(failedSession);
        client.QueueSession(recoveredSession);

        ServerlessActivityWorkerRegistrationHostedService service = new(
            client,
            options,
            ["RemoteHello"],
            NullLogger<ServerlessActivityWorkerRegistrationHostedService>.Instance,
            reconnectJitter: new DeterministicRandom(0.0));

        // Act
        await service.StartAsync(CancellationToken.None);
        await failedSession.WaitForMessageAsync(message => message.Start != null);
        await recoveredSession.WaitForMessageAsync(message => message.Start != null);
        await service.StopAsync(CancellationToken.None);

        // Assert
        client.SessionTaskHubs.Should().Equal(TaskHub, TaskHub);
    }

    [Fact]
    public async Task ServerlessActivityWorkerRegistrationHostedService_StopAsync_DoesNotCompleteStreamWhileWriteIsInFlight()
    {
        // Arrange
        ServerlessWorkerRuntimeOptions options = new()
        {
            Mode = ServerlessMode.ServerlessInclude,
            TaskHub = TaskHub,
            WorkerProfileId = "profile-a",
            MaxConcurrentActivities = 3,
            HeartbeatInterval = TimeSpan.FromMilliseconds(10),
        };

        FakeServerlessActivityWorkerSession session = new() { BlockWriteAttempt = 2 };
        FakeServerlessActivitiesClient client = new();
        client.QueueSession(session);

        ServerlessActivityWorkerRegistrationHostedService service = new(
            client,
            options,
            ["RemoteHello"],
            NullLogger<ServerlessActivityWorkerRegistrationHostedService>.Instance);

        // Act
        await service.StartAsync(CancellationToken.None);
        await session.WaitForBlockedWriteAsync();
        Task stopTask = service.StopAsync(CancellationToken.None);
        Task completeAttempt = session.WaitForCompleteAsync();
        Task completeBeforeWriteReleased = await Task.WhenAny(
            completeAttempt,
            Task.Delay(TimeSpan.FromMilliseconds(100)));
        session.ReleaseBlockedWrite();
        await stopTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        completeBeforeWriteReleased.Should().NotBe(completeAttempt);
        session.CompleteCalled.Should().BeTrue();
        session.CompleteCalledWhileWriteActive.Should().BeFalse();
    }

    [Fact]
    public async Task EnableServerlessActivities_ConfiguresLocalWorkerExclusionFilterFromWorkerProfiles()
    {
        // Arrange
        using EnvironmentVariableScope endpoint = new("DTS_ENDPOINT", "https://example.scheduler");
        using EnvironmentVariableScope taskHub = new("DTS_TASK_HUB", TaskHub);
        ServiceCollection services = new();
        services.Configure<DurableTaskSchedulerWorkerOptions>(
            Options.DefaultName,
            options => options.TaskHubName = TaskHub);
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        // Act
        mockBuilder.Object.EnableServerlessActivities();

        await using ServiceProvider provider = services.BuildServiceProvider();
        DurableTaskWorkerWorkItemFilters filters = provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>().Get(Options.DefaultName);

        // Assert
        filters.ExcludedActivities.Select(filter => filter.Name).Should().Contain("ConfiguredRemoteHello");
        filters.Activities.Should().BeEmpty();
    }

    [Fact]
    public async Task EnableServerlessActivities_RegistersDeclarationHostedService()
    {
        // Arrange
        using EnvironmentVariableScope endpoint = new("DTS_ENDPOINT", "https://example.scheduler");
        using EnvironmentVariableScope taskHub = new("DTS_TASK_HUB", TaskHub);
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        // Act
        mockBuilder.Object.EnableServerlessActivities();

        // Assert
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void EnableServerlessActivities_WhenRunningInServerlessWorker_Throws()
    {
        // Arrange
        using EnvironmentVariableScope substrate = new("DTS_SUBSTRATE", "Sandbox");
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        // Act
        Action action = () => mockBuilder.Object.EnableServerlessActivities();

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("EnableServerlessActivities is for declaring serverless activities from the coordinator app. DTS serverless workers should use UseServerlessWorker instead.");
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void ServerlessActivityDeclarationResolver_ResolveDeclarations_UsesWorkerProfileConfigure()
    {
        // Arrange
        using EnvironmentVariableScope image = new("DTS_SERVERLESS_ACTIVITY_IMAGE", "example.com/not-used:latest");
        using EnvironmentVariableScope cpu = new("DTS_SERVERLESS_CPU", "2000m");
        using EnvironmentVariableScope memory = new("DTS_SERVERLESS_MEMORY", "4096Mi");
        using EnvironmentVariableScope maxActivities = new("DTS_SERVERLESS_MAX_ACTIVITIES", "99");

        // Act
        ServerlessOptions options = ServerlessActivityDeclarationResolver.ResolveDeclarations(TaskHub)
            .Single(options => options.WorkerProfileId == "annotated-profile");
        ServerlessActivityDeclaration declaration = ServerlessActivityConfiguration.BuildDeclaration(
            options,
            ServerlessActivityConfiguration.ResolveActivityNames(options.ActivityNames));

        // Assert
        declaration.WorkerProfileId.Should().Be("annotated-profile");
        declaration.ActivityNames.Should().Equal("ConfiguredRemoteHello");
        declaration.Image.ImageRef.Should().Be("example.com/repo/annotated-worker:latest");
        declaration.Resources.Cpu.Should().Be("500m");
        declaration.Resources.Memory.Should().Be("1024Mi");
        declaration.MaxConcurrentActivities.Should().Be(4);
        declaration.EnvironmentVariables.Should().ContainKey("CUSTOM_ENV").WhoseValue.Should().Be("configured-value");
        declaration.Entrypoint.Should().BeEmpty();
        declaration.Cmd.Should().BeEmpty();
    }

    [Fact]
    public void ServerlessActivityDeclarationResolver_ResolveDeclaredActivityNames_UsesWorkerProfileConfigure()
    {
        // Arrange
        int before = AnnotatedWorkerProfile.ConfigureCallCount;

        // Act
        string[] activityNames = ServerlessActivityDeclarationResolver.ResolveDeclaredActivityNames(TaskHub);

        // Assert
        activityNames.Should().Contain("ConfiguredRemoteHello");
        AnnotatedWorkerProfile.ConfigureCallCount.Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task UseServerlessWorker_ConfiguresRegisteredActivityWorkerFilter()
    {
        // Arrange
        using EnvironmentVariableScope endpoint = new("DTS_ENDPOINT", "https://example.scheduler");
        using EnvironmentVariableScope taskHub = new("DTS_TASK_HUB", TaskHub);
        ServiceCollection services = new();
        services.Configure<DurableTaskRegistry>(
            Options.DefaultName,
            registry => registry.AddActivityFunc<string, string>(new TaskName("RemoteHello"), (_, input) => input));
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        // Act
        mockBuilder.Object.UseServerlessWorker();

        await using ServiceProvider provider = services.BuildServiceProvider();
        DurableTaskWorkerWorkItemFilters filters = provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>().Get(Options.DefaultName);

        // Assert
        filters.Activities.Select(filter => filter.Name).Should().Equal("RemoteHello");
        filters.ExcludedActivities.Should().BeEmpty();
        filters.Orchestrations.Should().BeEmpty();
        filters.Entities.Should().BeEmpty();
    }

    [Fact]
    public async Task UseServerlessWorker_ConfiguresSchedulerWithoutCredential()
    {
        // Arrange
        using EnvironmentVariableScope endpoint = new("DTS_ENDPOINT", "https://example.scheduler");
        using EnvironmentVariableScope taskHub = new("DTS_TASK_HUB", TaskHub);
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        // Act
        mockBuilder.Object.UseServerlessWorker();

        await using ServiceProvider provider = services.BuildServiceProvider();
        DurableTaskSchedulerWorkerOptions options = provider
            .GetRequiredService<IOptionsMonitor<DurableTaskSchedulerWorkerOptions>>()
            .Get(Options.DefaultName);

        // Assert
        options.EndpointAddress.Should().Be("https://example.scheduler");
        options.TaskHubName.Should().Be(TaskHub);
        options.Credential.Should().BeNull();
        options.AllowInsecureCredentials.Should().BeTrue();
    }

    [Fact]
    public void UseServerlessWorker_DoesNotRegisterWakeupServerHostedService()
    {
        // Arrange
        using EnvironmentVariableScope endpoint = new("DTS_ENDPOINT", "https://example.scheduler");
        using EnvironmentVariableScope taskHub = new("DTS_TASK_HUB", TaskHub);
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        // Act
        mockBuilder.Object.UseServerlessWorker();

        // Assert
        services.Count(descriptor => descriptor.ServiceType == typeof(IHostedService)).Should().Be(1);
    }

    [Fact]
    public void UseServerlessWorker_MissingInjectedEndpoint_Throws()
    {
        // Arrange
        using EnvironmentVariableScope endpoint = new("DTS_ENDPOINT", null);
        using EnvironmentVariableScope taskHub = new("DTS_TASK_HUB", TaskHub);
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        // Act
        Action action = () => mockBuilder.Object.UseServerlessWorker();

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("DTS_ENDPOINT must be injected by DTS for serverless workers.");
    }

    [Fact]
    public void UseServerlessWorker_MissingInjectedTaskHub_Throws()
    {
        // Arrange
        using EnvironmentVariableScope endpoint = new("DTS_ENDPOINT", "https://example.scheduler");
        using EnvironmentVariableScope taskHub = new("DTS_TASK_HUB", null);
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        // Act
        Action action = () => mockBuilder.Object.UseServerlessWorker();

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("DTS_TASK_HUB must be injected by DTS for serverless workers.");
    }

    [ServerlessWorkerProfile("annotated-profile")]
    sealed class AnnotatedWorkerProfile : IServerlessWorkerProfile
    {
        public static int ConfigureCallCount { get; private set; }

        public void Configure(ServerlessOptions options)
        {
            ConfigureCallCount++;
            options.ContainerImage = "example.com/repo/annotated-worker:latest";
            options.Cpu = "500m";
            options.Memory = "1024Mi";
            options.MaxConcurrentActivities = 4;
            options.EnvironmentVariables["CUSTOM_ENV"] = "configured-value";
            options.AddActivity("ConfiguredRemoteHello");
        }
    }

    sealed class FakeServerlessActivitiesClient : IServerlessActivitiesClient
    {
        readonly Queue<FakeServerlessActivityWorkerSession> queuedSessions = new();

        public int TransientDeclarationFailures { get; init; }

        public int DeclarationAttempts { get; private set; }

        public List<ServerlessActivityDeclaration> Declarations { get; } = [];

        public List<string> DeclarationTaskHubs { get; } = [];

        public List<string> SessionTaskHubs { get; } = [];

        public List<FakeServerlessActivityWorkerSession> Sessions { get; } = [];

        public FakeServerlessActivityWorkerSession Session { get; } = new();

        public void QueueSession(FakeServerlessActivityWorkerSession session) => this.queuedSessions.Enqueue(session);

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
            FakeServerlessActivityWorkerSession session = this.queuedSessions.Count > 0
                ? this.queuedSessions.Dequeue()
                : this.Session;
            this.Sessions.Add(session);
            return session;
        }
    }

    sealed class RecordingServerlessActivitiesCallInvoker : CallInvoker
    {
        public Metadata DeclarationHeaders { get; private set; } = [];

        public Metadata WorkerSessionHeaders { get; private set; } = [];

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request)
        {
            throw new NotSupportedException();
        }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request)
        {
            method.FullName.Should().EndWith("/DeclareServerlessActivities");
            this.DeclarationHeaders = options.Headers ?? [];

            return new AsyncUnaryCall<TResponse>(
                Task.FromResult((TResponse)(object)new ServerlessActivityDeclarationResult()),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, string.Empty),
                () => [],
                () => { });
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request)
        {
            throw new NotSupportedException();
        }

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options)
        {
            method.FullName.Should().EndWith("/ConnectServerlessActivityWorker");
            this.WorkerSessionHeaders = options.Headers ?? [];

            return new AsyncClientStreamingCall<TRequest, TResponse>(
                new RecordingClientStreamWriter<TRequest>(),
                Task.FromResult((TResponse)(object)new ServerlessActivityWorkerSessionResult()),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, string.Empty),
                () => [],
                () => { });
        }

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options)
        {
            throw new NotSupportedException();
        }
    }

    sealed class RecordingClientStreamWriter<T> : IClientStreamWriter<T>
    {
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(T message) => Task.CompletedTask;

        public Task CompleteAsync() => Task.CompletedTask;
    }

    sealed class FakeServerlessActivityWorkerSession : IServerlessActivityWorkerSession
    {
        readonly object sync = new();
        readonly TaskCompletionSource<ServerlessActivityWorkerSessionResult> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource blockedWriteStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource releaseBlockedWrite =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        int writeAttempts;
        int activeWrites;

        public List<ServerlessActivityWorkerMessage> Messages { get; } = [];

        public int? ThrowOnWriteAttempt { get; init; }

        public int? BlockWriteAttempt { get; init; }

        public bool CompleteCalled { get; private set; }

        public bool CompleteCalledWhileWriteActive { get; private set; }

        public void FailCompletion(Exception exception) => this.completion.TrySetException(exception);

        public Task WaitForBlockedWriteAsync() => this.blockedWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public Task WaitForCompleteAsync()
        {
            lock (this.sync)
            {
                return this.CompleteCalled ? Task.CompletedTask : this.completion.Task;
            }
        }

        public void ReleaseBlockedWrite() => this.releaseBlockedWrite.TrySetResult();

        public async Task WaitForMessageAsync(Func<ServerlessActivityWorkerMessage, bool> predicate)
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
            while (!timeout.IsCancellationRequested)
            {
                lock (this.sync)
                {
                    if (this.Messages.Any(predicate))
                    {
                        return;
                    }
                }

                await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
            }

            throw new TimeoutException("Timed out waiting for serverless worker message.");
        }

        public Task WriteMessageAsync(ServerlessActivityWorkerMessage message)
        {
            int attempt;
            bool blockWrite;
            lock (this.sync)
            {
                attempt = ++this.writeAttempts;
                if (this.ThrowOnWriteAttempt == attempt)
                {
                    throw new RpcException(new Status(StatusCode.Unavailable, "transient"));
                }

                this.activeWrites++;
                blockWrite = this.BlockWriteAttempt == attempt;
                if (blockWrite)
                {
                    this.blockedWriteStarted.TrySetResult();
                }
            }

            return this.WriteMessageCoreAsync(message, blockWrite);
        }

        public Task<ServerlessActivityWorkerSessionResult> WaitForCompletionAsync() => this.completion.Task;

        public async Task CompleteAsync()
        {
            lock (this.sync)
            {
                this.CompleteCalled = true;
                this.CompleteCalledWhileWriteActive = this.activeWrites > 0;
            }

            this.completion.TrySetResult(new ServerlessActivityWorkerSessionResult { Accepted = true });
            await this.completion.Task.ConfigureAwait(false);
        }

        public ValueTask DisposeAsync() => default;

        async Task WriteMessageCoreAsync(ServerlessActivityWorkerMessage message, bool blockWrite)
        {
            try
            {
                if (blockWrite)
                {
                    await this.releaseBlockedWrite.Task.ConfigureAwait(false);
                }

                lock (this.sync)
                {
                    this.Messages.Add(message.Clone());
                }
            }
            finally
            {
                lock (this.sync)
                {
                    this.activeWrites--;
                }
            }
        }
    }

    sealed class DeterministicRandom : Random
    {
        readonly double value;

        public DeterministicRandom(double value)
        {
            this.value = value;
        }

        protected override double Sample() => this.value;
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
