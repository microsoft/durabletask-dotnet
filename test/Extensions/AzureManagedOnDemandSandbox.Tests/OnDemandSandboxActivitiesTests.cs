// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Azure.Identity;
using FluentAssertions;
using Grpc.Core;
using Microsoft.DurableTask.AzureManaged.OnDemandSandbox;
using Microsoft.DurableTask.Protobuf.OnDemandSandbox;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.DurableTask.Worker.AzureManaged.OnDemandSandbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.DurableTask.Worker.AzureManaged.Tests;

public class OnDemandSandboxActivitiesTests
{
    const string TaskHub = "testhub";

    [Fact]
    public void OnDemandSandboxDeclarationContract_DoesNotExposeRemovedOptions()
    {
        typeof(OnDemandSandboxOptions).GetProperty("LaunchCommand").Should().BeNull();
        typeof(OnDemandSandboxOptions).GetProperty("DeclarationRetryMaxAttempts").Should().BeNull();
        typeof(OnDemandSandboxOptions).GetProperty("DeclarationRetryDelay").Should().BeNull();
        typeof(OnDemandSandboxOptions).GetProperty(
            "HeartbeatInterval",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Should().BeNull();
        typeof(OnDemandSandboxOptions).GetProperty("WakeupPort").Should().BeNull();
        typeof(OnDemandSandboxOptions).GetProperty(
            "WorkerRegistrationRetryInitialDelay",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Should().BeNull();
        typeof(OnDemandSandboxOptions).GetProperty(
            "WorkerRegistrationRetryMaxDelay",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Should().BeNull();
        typeof(OnDemandSandboxOptions).GetProperty(
            "Mode",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Should().BeNull();
        typeof(OnDemandSandboxActivityDeclaration).GetProperty("LaunchCommand").Should().BeNull();
    }

    [Fact]
    public void OnDemandSandboxDeclarationContract_ExposesProfileAddActivityOnly()
    {
        // Arrange
        Type optionsType = typeof(OnDemandSandboxOptions);
        Type? activityAttributeType = typeof(OnDemandSandboxOptions).Assembly.GetType(
            "Microsoft.DurableTask.Worker.AzureManaged.OnDemandSandbox.OnDemandSandboxActivityAttribute");

        // Act/Assert
        optionsType.GetProperty("ActivityNames").Should().BeNull();
        optionsType.GetMethod("AddActivity", [typeof(string)]).Should().NotBeNull();
        optionsType.GetMethods().Should().Contain(method =>
            method.Name == "AddActivity" && method.IsGenericMethodDefinition);
        activityAttributeType.Should().BeNull();
    }

    [Theory]
    [InlineData("500m", "1024Mi")]
    [InlineData("0.5", "1Gi")]
    [InlineData("2", "2048")]
    public void OnDemandSandboxActivityDeclarationBuilder_BuildDeclaration_AcceptsAdcResourceQuantities(
        string cpu,
        string memory)
    {
        // Arrange
        OnDemandSandboxOptions options = CreateDeclarationOptions();
        options.Cpu = cpu;
        options.Memory = memory;

        // Act
        OnDemandSandboxActivityDeclaration declaration = OnDemandSandboxActivityDeclarationBuilder.BuildDeclaration(
            options,
            OnDemandSandboxActivityDeclarationBuilder.ResolveActivityNames(options.ActivityNames));

        // Assert
        declaration.Resources.Cpu.Should().Be(cpu);
        declaration.Resources.Memory.Should().Be(memory);
    }

    [Theory]
    [InlineData("0", "1024Mi", "CPU")]
    [InlineData("0m", "1024Mi", "CPU")]
    [InlineData("500Mi", "1024Mi", "CPU")]
    [InlineData("500m", "0", "memory")]
    [InlineData("500m", "0Mi", "memory")]
    [InlineData("500m", "500m", "memory")]
    public void OnDemandSandboxActivityDeclarationBuilder_BuildDeclaration_RejectsInvalidAdcResourceQuantities(
        string cpu,
        string memory,
        string expectedMessage)
    {
        // Arrange
        OnDemandSandboxOptions options = CreateDeclarationOptions();
        options.Cpu = cpu;
        options.Memory = memory;

        // Act
        Action action = () => OnDemandSandboxActivityDeclarationBuilder.BuildDeclaration(
            options,
            OnDemandSandboxActivityDeclarationBuilder.ResolveActivityNames(options.ActivityNames));

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{expectedMessage}*");
    }

    [Fact]
    public void OnDemandSandboxActivityDeclarationBuilder_BuildDeclaration_RequiresSchedulerManagedIdentityClientId()
    {
        // Arrange
        OnDemandSandboxOptions options = CreateDeclarationOptions();
        options.SchedulerManagedIdentityClientId = " ";

        // Act
        Action action = () => OnDemandSandboxActivityDeclarationBuilder.BuildDeclaration(
            options,
            OnDemandSandboxActivityDeclarationBuilder.ResolveActivityNames(options.ActivityNames));

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*managed identity client ID*");
    }

    [Fact]
    public void OnDemandSandboxActivityDeclarationBuilder_BuildDeclaration_RequiresImagePullManagedIdentityClientId()
    {
        // Arrange
        OnDemandSandboxOptions options = CreateDeclarationOptions();
        options.ImagePullManagedIdentityClientId = " ";

        // Act
        Action action = () => OnDemandSandboxActivityDeclarationBuilder.BuildDeclaration(
            options,
            OnDemandSandboxActivityDeclarationBuilder.ResolveActivityNames(options.ActivityNames));

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*managed identity client ID ADC uses to pull the worker image*");
    }

    [Fact]
    public async Task OnDemandSandboxActivitiesGrpcTransport_SendsTaskHubMetadata()
    {
        // Arrange
        RecordingOnDemandSandboxActivitiesCallInvoker callInvoker = new();
        OnDemandSandboxActivitiesGrpcTransport transport = new(new OnDemandSandboxActivities.OnDemandSandboxActivitiesClient(callInvoker));
        OnDemandSandboxActivityDeclaration declaration = new()
        {
            WorkerProfileId = "profile-a",
            Image = new OnDemandSandboxActivityImage
            {
                ImageRef = "example.com/repo/worker:latest",
            },
            Resources = new OnDemandSandboxActivityResources
            {
                Cpu = "500m",
                Memory = "1024Mi",
            },
            MaxConcurrentActivities = 7,
        };
        declaration.ActivityNames.Add("RemoteHello");

        // Act
        await transport.DeclareOnDemandSandboxActivitiesAsync(declaration, TaskHub, CancellationToken.None);
        await using IOnDemandSandboxActivityWorkerSession session = transport.OpenOnDemandSandboxActivityWorkerSession(
            TaskHub,
            CancellationToken.None);

        // Assert
        callInvoker.DeclarationHeaders.Should().Contain(header => header.Key == "taskhub" && header.Value == TaskHub);
        callInvoker.WorkerSessionHeaders.Should().Contain(header => header.Key == "taskhub" && header.Value == TaskHub);
    }

    [Fact]
    public async Task OnDemandSandboxActivitiesGrpcTransport_CanRelyOnChannelTaskHubMetadata()
    {
        // Arrange
        RecordingOnDemandSandboxActivitiesCallInvoker callInvoker = new();
        OnDemandSandboxActivitiesGrpcTransport transport = new(
            new OnDemandSandboxActivities.OnDemandSandboxActivitiesClient(callInvoker),
            attachTaskHubMetadata: false);
        OnDemandSandboxActivityDeclaration declaration = new()
        {
            WorkerProfileId = "profile-a",
            Image = new OnDemandSandboxActivityImage
            {
                ImageRef = "example.com/repo/worker:latest",
            },
            Resources = new OnDemandSandboxActivityResources
            {
                Cpu = "500m",
                Memory = "1024Mi",
            },
            MaxConcurrentActivities = 7,
        };
        declaration.ActivityNames.Add("RemoteHello");

        // Act
        await transport.DeclareOnDemandSandboxActivitiesAsync(declaration, TaskHub, CancellationToken.None);
        await using IOnDemandSandboxActivityWorkerSession session = transport.OpenOnDemandSandboxActivityWorkerSession(
            TaskHub,
            CancellationToken.None);

        // Assert
        callInvoker.DeclarationHeaders.Should().NotContain(header => header.Key == "taskhub");
        callInvoker.WorkerSessionHeaders.Should().NotContain(header => header.Key == "taskhub");
    }

    [Fact]
    public async Task OnDemandSandboxActivityWorkerRegistrationHostedService_SendsLiveWorkerMetadataWithRegisteredActivities()
    {
        // Arrange
        string? originalSubstrate = Environment.GetEnvironmentVariable("DTS_SUBSTRATE");
        string? originalSandboxId = Environment.GetEnvironmentVariable("DTS_SANDBOX_ID");
        Environment.SetEnvironmentVariable("DTS_SUBSTRATE", "Sandbox");
        Environment.SetEnvironmentVariable("DTS_SANDBOX_ID", "sandbox-1");

        try
        {
            OnDemandSandboxWorkerRuntimeOptions options = new()
            {
                Mode = OnDemandSandboxMode.OnDemandSandboxInclude,
                TaskHub = TaskHub,
                WorkerProfileId = "profile-a",
                MaxConcurrentActivities = 3,
                HeartbeatInterval = TimeSpan.FromDays(1),
            };
            FakeOnDemandSandboxActivitiesTransport client = new();
            OnDemandSandboxActivityWorkerRegistrationHostedService service = new(
                client,
                options,
                ["RemoteHello"],
                NullLogger<OnDemandSandboxActivityWorkerRegistrationHostedService>.Instance);

            // Act
            await service.StartAsync(CancellationToken.None);
            await client.Session.WaitForMessageAsync(message => message.Start != null);
            await service.StopAsync(CancellationToken.None);

            // Assert
            client.SessionTaskHubs.Should().Equal(TaskHub);
            OnDemandSandboxActivityWorkerMessage message = client.Session.Messages.Should().ContainSingle().Subject;
            OnDemandSandboxActivityWorkerStart start = message.Start;
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
    public void OnDemandSandboxActivityTracker_TracksInFlightActivityCount()
    {
        // Arrange
        OnDemandSandboxActivityTracker activityTracker = new();

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
    public async Task OnDemandSandboxActivityWorkerRegistrationHostedService_SendsHeartbeatWithCurrentInFlightCount()
    {
        // Arrange
        OnDemandSandboxWorkerRuntimeOptions options = new()
        {
            Mode = OnDemandSandboxMode.OnDemandSandboxInclude,
            TaskHub = TaskHub,
            WorkerProfileId = "profile-a",
            MaxConcurrentActivities = 3,
            HeartbeatInterval = TimeSpan.FromMilliseconds(10),
        };

        FakeOnDemandSandboxActivitiesTransport client = new();
        OnDemandSandboxActivityTracker activityTracker = new();
        activityTracker.NotifyActivityStarted();
        activityTracker.NotifyActivityStarted();

        OnDemandSandboxActivityWorkerRegistrationHostedService service = new(
            client,
            options,
            ["RemoteHello"],
            NullLogger<OnDemandSandboxActivityWorkerRegistrationHostedService>.Instance,
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
    public async Task OnDemandSandboxActivityWorkerRegistrationHostedService_ReopensSessionAfterTransientStreamFailure()
    {
        // Arrange
        OnDemandSandboxWorkerRuntimeOptions options = new()
        {
            Mode = OnDemandSandboxMode.OnDemandSandboxInclude,
            TaskHub = TaskHub,
            WorkerProfileId = "profile-a",
            MaxConcurrentActivities = 3,
            HeartbeatInterval = TimeSpan.FromMilliseconds(10),
            WorkerRegistrationRetryInitialDelay = TimeSpan.FromMilliseconds(10),
            WorkerRegistrationRetryMaxDelay = TimeSpan.FromMilliseconds(10),
        };

        FakeOnDemandSandboxActivityWorkerSession failedSession = new() { ThrowOnWriteAttempt = 2 };
        FakeOnDemandSandboxActivityWorkerSession recoveredSession = new();
        FakeOnDemandSandboxActivitiesTransport client = new();
        client.QueueSession(failedSession);
        client.QueueSession(recoveredSession);

        OnDemandSandboxActivityWorkerRegistrationHostedService service = new(
            client,
            options,
            ["RemoteHello"],
            NullLogger<OnDemandSandboxActivityWorkerRegistrationHostedService>.Instance);

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
    public async Task OnDemandSandboxActivityWorkerRegistrationHostedService_ReopensSessionAfterTerminalServerFailure()
    {
        // Arrange
        OnDemandSandboxWorkerRuntimeOptions options = new()
        {
            Mode = OnDemandSandboxMode.OnDemandSandboxInclude,
            TaskHub = TaskHub,
            WorkerProfileId = "profile-a",
            MaxConcurrentActivities = 3,
            HeartbeatInterval = TimeSpan.FromDays(1),
            WorkerRegistrationRetryInitialDelay = TimeSpan.FromMilliseconds(10),
            WorkerRegistrationRetryMaxDelay = TimeSpan.FromMilliseconds(10),
        };

        FakeOnDemandSandboxActivityWorkerSession failedSession = new();
        FakeOnDemandSandboxActivityWorkerSession recoveredSession = new();
        FakeOnDemandSandboxActivitiesTransport client = new();
        client.QueueSession(failedSession);
        client.QueueSession(recoveredSession);

        OnDemandSandboxActivityWorkerRegistrationHostedService service = new(
            client,
            options,
            ["RemoteHello"],
            NullLogger<OnDemandSandboxActivityWorkerRegistrationHostedService>.Instance);

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
    public void OnDemandSandboxActivityWorkerRegistrationHostedService_ComputeJitteredReconnectDelay_UsesFullJitterWindow()
    {
        // Arrange
        TimeSpan retryDelay = TimeSpan.FromSeconds(10);

        // Act
        TimeSpan zero = OnDemandSandboxActivityWorkerRegistrationHostedService.ComputeJitteredReconnectDelay(
            TimeSpan.Zero,
            new DeterministicRandom(0.5));
        TimeSpan low = OnDemandSandboxActivityWorkerRegistrationHostedService.ComputeJitteredReconnectDelay(
            retryDelay,
            new DeterministicRandom(0.0));
        TimeSpan mid = OnDemandSandboxActivityWorkerRegistrationHostedService.ComputeJitteredReconnectDelay(
            retryDelay,
            new DeterministicRandom(0.5));
        TimeSpan high = OnDemandSandboxActivityWorkerRegistrationHostedService.ComputeJitteredReconnectDelay(
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
    public async Task OnDemandSandboxActivityWorkerRegistrationHostedService_AppliesJitterToReconnectDelay()
    {
        // Arrange
        OnDemandSandboxWorkerRuntimeOptions options = new()
        {
            Mode = OnDemandSandboxMode.OnDemandSandboxInclude,
            TaskHub = TaskHub,
            WorkerProfileId = "profile-a",
            MaxConcurrentActivities = 3,
            HeartbeatInterval = TimeSpan.FromMilliseconds(10),
            WorkerRegistrationRetryInitialDelay = TimeSpan.FromDays(1),
            WorkerRegistrationRetryMaxDelay = TimeSpan.FromDays(1),
        };

        FakeOnDemandSandboxActivityWorkerSession failedSession = new() { ThrowOnWriteAttempt = 2 };
        FakeOnDemandSandboxActivityWorkerSession recoveredSession = new();
        FakeOnDemandSandboxActivitiesTransport client = new();
        client.QueueSession(failedSession);
        client.QueueSession(recoveredSession);

        OnDemandSandboxActivityWorkerRegistrationHostedService service = new(
            client,
            options,
            ["RemoteHello"],
            NullLogger<OnDemandSandboxActivityWorkerRegistrationHostedService>.Instance,
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
    public async Task OnDemandSandboxActivityWorkerRegistrationHostedService_StopAsync_DoesNotCompleteStreamWhileWriteIsInFlight()
    {
        // Arrange
        OnDemandSandboxWorkerRuntimeOptions options = new()
        {
            Mode = OnDemandSandboxMode.OnDemandSandboxInclude,
            TaskHub = TaskHub,
            WorkerProfileId = "profile-a",
            MaxConcurrentActivities = 3,
            HeartbeatInterval = TimeSpan.FromMilliseconds(10),
        };

        FakeOnDemandSandboxActivityWorkerSession session = new() { BlockWriteAttempt = 2 };
        FakeOnDemandSandboxActivitiesTransport client = new();
        client.QueueSession(session);

        OnDemandSandboxActivityWorkerRegistrationHostedService service = new(
            client,
            options,
            ["RemoteHello"],
            NullLogger<OnDemandSandboxActivityWorkerRegistrationHostedService>.Instance);

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
    public void OnDemandSandboxActivityDeclarationResolver_ResolveDeclarations_UsesWorkerProfileConfigure()
    {
        // Arrange
        using EnvironmentVariableScope image = new("DTS_ON_DEMAND_SANDBOX_ACTIVITY_IMAGE", "example.com/not-used:latest");
        using EnvironmentVariableScope cpu = new("DTS_ON_DEMAND_SANDBOX_CPU", "2000m");
        using EnvironmentVariableScope memory = new("DTS_ON_DEMAND_SANDBOX_MEMORY", "4096Mi");
        using EnvironmentVariableScope maxActivities = new("DTS_ON_DEMAND_SANDBOX_MAX_ACTIVITIES", "99");

        // Act
        OnDemandSandboxOptions options = OnDemandSandboxActivityDeclarationResolver.ResolveDeclarations(TaskHub)
            .Single(options => options.WorkerProfileId == "annotated-profile");
        OnDemandSandboxActivityDeclaration declaration = OnDemandSandboxActivityDeclarationBuilder.BuildDeclaration(
            options,
            OnDemandSandboxActivityDeclarationBuilder.ResolveActivityNames(options.ActivityNames));

        // Assert
        declaration.WorkerProfileId.Should().Be("annotated-profile");
        declaration.ActivityNames.Should().Equal("ConfiguredRemoteHello");
        declaration.Image.ImageRef.Should().Be("example.com/repo/annotated-worker:latest");
        declaration.Image.ManagedIdentityClientId.Should().Be("image-pull-client-id");
        declaration.SchedulerManagedIdentityClientId.Should().Be("scheduler-client-id");
        declaration.Resources.Cpu.Should().Be("500m");
        declaration.Resources.Memory.Should().Be("1024Mi");
        declaration.MaxConcurrentActivities.Should().Be(4);
        declaration.EnvironmentVariables.Should().ContainKey("CUSTOM_ENV").WhoseValue.Should().Be("configured-value");
        declaration.Entrypoint.Should().BeEmpty();
        declaration.Cmd.Should().BeEmpty();
    }

    [Fact]
    public async Task UseSandboxWorker_ConfiguresRegisteredActivityWorkerFilter()
    {
        // Arrange
        using EnvironmentVariableScope endpoint = new("DTS_ENDPOINT", "https://example.scheduler");
        using EnvironmentVariableScope taskHub = new("DTS_TASK_HUB", TaskHub);
        using EnvironmentVariableScope maxActivities = new("DTS_ON_DEMAND_SANDBOX_MAX_ACTIVITIES", "3");
        ServiceCollection services = new();
        services.Configure<DurableTaskRegistry>(
            Options.DefaultName,
            registry => registry.AddActivityFunc<string, string>(new TaskName("RemoteHello"), (_, input) => input));
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        // Act
        mockBuilder.Object.UseSandboxWorker();

        await using ServiceProvider provider = services.BuildServiceProvider();
        DurableTaskWorkerWorkItemFilters filters = provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>().Get(Options.DefaultName);
        DurableTaskWorkerOptions workerOptions = provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerOptions>>().Get(Options.DefaultName);

        // Assert
        filters.Activities.Select(filter => filter.Name).Should().Equal("RemoteHello");
        filters.Orchestrations.Should().BeEmpty();
        filters.Entities.Should().BeEmpty();
        workerOptions.Concurrency.MaximumConcurrentActivityWorkItems.Should().Be(3);
        workerOptions.Concurrency.MaximumConcurrentOrchestrationWorkItems.Should().Be(0);
        workerOptions.Concurrency.MaximumConcurrentEntityWorkItems.Should().Be(0);
    }

    [Fact]
    public async Task UseSandboxWorker_WithNoRegisteredActivities_FailsWhenWorkerFiltersAreResolved()
    {
        // Arrange
        using EnvironmentVariableScope endpoint = new("DTS_ENDPOINT", "https://example.scheduler");
        using EnvironmentVariableScope taskHub = new("DTS_TASK_HUB", TaskHub);
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        mockBuilder.Object.UseSandboxWorker();

        await using ServiceProvider provider = services.BuildServiceProvider();

        // Act
        Action act = () => provider
            .GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>()
            .Get(Options.DefaultName);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("On-demand sandbox workers require at least one registered activity*");
    }

    [Fact]
    public async Task UseSandboxWorker_ConfiguresSchedulerWithoutCredential()
    {
        // Arrange
        using EnvironmentVariableScope endpoint = new("DTS_ENDPOINT", "https://example.scheduler");
        using EnvironmentVariableScope taskHub = new("DTS_TASK_HUB", TaskHub);
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        // Act
        mockBuilder.Object.UseSandboxWorker();

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
    public async Task UseSandboxWorker_WithManagedIdentityAuth_ConfiguresSchedulerCredential()
    {
        // Arrange
        using EnvironmentVariableScope endpoint = new("DTS_ENDPOINT", "https://example.scheduler");
        using EnvironmentVariableScope taskHub = new("DTS_TASK_HUB", TaskHub);
        using EnvironmentVariableScope auth = new("DTS_AUTHENTICATION", "ManagedIdentity");
        using EnvironmentVariableScope clientId = new("DTS_UMI_CLIENT_ID", "worker-client-id");
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        // Act
        mockBuilder.Object.UseSandboxWorker();

        await using ServiceProvider provider = services.BuildServiceProvider();
        DurableTaskSchedulerWorkerOptions options = provider
            .GetRequiredService<IOptionsMonitor<DurableTaskSchedulerWorkerOptions>>()
            .Get(Options.DefaultName);

        // Assert
        options.EndpointAddress.Should().Be("https://example.scheduler");
        options.TaskHubName.Should().Be(TaskHub);
        options.Credential.Should().BeOfType<ManagedIdentityCredential>();
        options.AllowInsecureCredentials.Should().BeFalse();
    }

    [Fact]
    public async Task UseSandboxWorker_WithManagedIdentityAuthAndMissingClientId_Throws()
    {
        // Arrange
        using EnvironmentVariableScope endpoint = new("DTS_ENDPOINT", "https://example.scheduler");
        using EnvironmentVariableScope taskHub = new("DTS_TASK_HUB", TaskHub);
        using EnvironmentVariableScope auth = new("DTS_AUTHENTICATION", "ManagedIdentity");
        using EnvironmentVariableScope clientId = new("DTS_UMI_CLIENT_ID", null);
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        // Act
        mockBuilder.Object.UseSandboxWorker();
        await using ServiceProvider provider = services.BuildServiceProvider();
        Action getOptions = () => provider
            .GetRequiredService<IOptionsMonitor<DurableTaskSchedulerWorkerOptions>>()
            .Get(Options.DefaultName);

        // Assert
        getOptions.Should().Throw<InvalidOperationException>()
            .WithMessage("*DTS_UMI_CLIENT_ID*");
    }

    [Fact]
    public void UseSandboxWorker_DoesNotRegisterWakeupServerHostedService()
    {
        // Arrange
        using EnvironmentVariableScope endpoint = new("DTS_ENDPOINT", "https://example.scheduler");
        using EnvironmentVariableScope taskHub = new("DTS_TASK_HUB", TaskHub);
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        // Act
        mockBuilder.Object.UseSandboxWorker();

        // Assert
        services.Count(descriptor => descriptor.ServiceType == typeof(IHostedService)).Should().Be(1);
    }

    [Fact]
    public void UseSandboxWorker_MissingInjectedEndpoint_Throws()
    {
        // Arrange
        using EnvironmentVariableScope endpoint = new("DTS_ENDPOINT", null);
        using EnvironmentVariableScope taskHub = new("DTS_TASK_HUB", TaskHub);
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        // Act
        Action action = () => mockBuilder.Object.UseSandboxWorker();

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("DTS_ENDPOINT must be injected by DTS for on-demand sandbox workers.");
    }

    [Fact]
    public void UseSandboxWorker_MissingInjectedTaskHub_Throws()
    {
        // Arrange
        using EnvironmentVariableScope endpoint = new("DTS_ENDPOINT", "https://example.scheduler");
        using EnvironmentVariableScope taskHub = new("DTS_TASK_HUB", null);
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        // Act
        Action action = () => mockBuilder.Object.UseSandboxWorker();

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("DTS_TASK_HUB must be injected by DTS for on-demand sandbox workers.");
    }

    static OnDemandSandboxOptions CreateDeclarationOptions()
    {
        OnDemandSandboxOptions options = new()
        {
            TaskHub = TaskHub,
            WorkerProfileId = "profile-a",
            ContainerImage = "mcr.microsoft.com/durabletask/demo-worker:1.0",
            ImagePullManagedIdentityClientId = "image-pull-client-id",
            SchedulerManagedIdentityClientId = "scheduler-client-id",
            Cpu = "500m",
            Memory = "1024Mi",
            MaxConcurrentActivities = 7,
        };
        options.AddActivity("RemoteHello");
        return options;
    }

    [OnDemandSandboxWorkerProfile("annotated-profile")]
    sealed class AnnotatedWorkerProfile : ISandboxWorkerProfile
    {
        public static int ConfigureCallCount { get; private set; }

        public void Configure(OnDemandSandboxOptions options)
        {
            ConfigureCallCount++;
            options.ContainerImage = "example.com/repo/annotated-worker:latest";
            options.ImagePullManagedIdentityClientId = "image-pull-client-id";
            options.SchedulerManagedIdentityClientId = "scheduler-client-id";
            options.Cpu = "500m";
            options.Memory = "1024Mi";
            options.MaxConcurrentActivities = 4;
            options.EnvironmentVariables["CUSTOM_ENV"] = "configured-value";
            options.AddActivity("ConfiguredRemoteHello");
        }
    }

    sealed class FakeOnDemandSandboxActivitiesTransport : IOnDemandSandboxActivitiesTransport
    {
        readonly Queue<FakeOnDemandSandboxActivityWorkerSession> queuedSessions = new();

        public int TransientDeclarationFailures { get; init; }

        public int DeclarationAttempts { get; private set; }

        public List<OnDemandSandboxActivityDeclaration> Declarations { get; } = [];

        public List<string> DeclarationTaskHubs { get; } = [];

        public List<string> SessionTaskHubs { get; } = [];

        public List<FakeOnDemandSandboxActivityWorkerSession> Sessions { get; } = [];

        public FakeOnDemandSandboxActivityWorkerSession Session { get; } = new();

        public void QueueSession(FakeOnDemandSandboxActivityWorkerSession session) => this.queuedSessions.Enqueue(session);

        public Task<OnDemandSandboxActivityDeclarationResult> DeclareOnDemandSandboxActivitiesAsync(
            OnDemandSandboxActivityDeclaration declaration,
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
            return Task.FromResult(new OnDemandSandboxActivityDeclarationResult());
        }

        public Task<RemoveOnDemandSandboxActivityDeclarationResult> RemoveOnDemandSandboxActivityDeclarationAsync(
            string workerProfileId,
            string taskHub,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new RemoveOnDemandSandboxActivityDeclarationResult());
        }

        public IOnDemandSandboxActivityWorkerSession OpenOnDemandSandboxActivityWorkerSession(string taskHub, CancellationToken cancellationToken)
        {
            this.SessionTaskHubs.Add(taskHub);
            FakeOnDemandSandboxActivityWorkerSession session = this.queuedSessions.Count > 0
                ? this.queuedSessions.Dequeue()
                : this.Session;
            this.Sessions.Add(session);
            return session;
        }
    }

    sealed class RecordingOnDemandSandboxActivitiesCallInvoker : CallInvoker
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
            method.FullName.Should().EndWith("/DeclareOnDemandSandboxActivities");
            this.DeclarationHeaders = options.Headers ?? [];

            return new AsyncUnaryCall<TResponse>(
                Task.FromResult((TResponse)(object)new OnDemandSandboxActivityDeclarationResult()),
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
            method.FullName.Should().EndWith("/ConnectOnDemandSandboxActivityWorker");
            this.WorkerSessionHeaders = options.Headers ?? [];

            return new AsyncClientStreamingCall<TRequest, TResponse>(
                new RecordingClientStreamWriter<TRequest>(),
                Task.FromResult((TResponse)(object)new OnDemandSandboxActivityWorkerSessionResult()),
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

    sealed class FakeOnDemandSandboxActivityWorkerSession : IOnDemandSandboxActivityWorkerSession
    {
        readonly object sync = new();
        readonly TaskCompletionSource<OnDemandSandboxActivityWorkerSessionResult> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource blockedWriteStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource releaseBlockedWrite =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        int writeAttempts;
        int activeWrites;

        public List<OnDemandSandboxActivityWorkerMessage> Messages { get; } = [];

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

        public async Task WaitForMessageAsync(Func<OnDemandSandboxActivityWorkerMessage, bool> predicate)
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

            throw new TimeoutException("Timed out waiting for on-demand sandbox worker message.");
        }

        public Task WriteMessageAsync(OnDemandSandboxActivityWorkerMessage message)
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

        public Task<OnDemandSandboxActivityWorkerSessionResult> WaitForCompletionAsync() => this.completion.Task;

        public async Task CompleteAsync()
        {
            lock (this.sync)
            {
                this.CompleteCalled = true;
                this.CompleteCalledWhileWriteActive = this.activeWrites > 0;
            }

            this.completion.TrySetResult(new OnDemandSandboxActivityWorkerSessionResult());
            await this.completion.Task.ConfigureAwait(false);
        }

        public ValueTask DisposeAsync() => default;

        async Task WriteMessageCoreAsync(OnDemandSandboxActivityWorkerMessage message, bool blockWrite)
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
