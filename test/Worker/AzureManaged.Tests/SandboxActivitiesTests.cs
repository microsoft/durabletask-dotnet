// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Identity;
using FluentAssertions;
using Grpc.Core;
using Microsoft.DurableTask.AzureManaged.Internal;
using Microsoft.DurableTask.Protobuf.Sandboxes;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.DurableTask.Worker.AzureManaged.Sandboxes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.DurableTask.Worker.AzureManaged.Tests;

public class SandboxActivitiesTests
{
    const string TaskHub = "testhub";

    [Fact]
    public async Task SandboxActivitiesGrpcTransport_SendsTaskHubMetadata()
    {
        // Arrange
        RecordingSandboxActivitiesCallInvoker callInvoker = new();
        SandboxActivitiesGrpcTransport transport = new(new SandboxActivities.SandboxActivitiesClient(callInvoker));
        SandboxWorkerProfile workerProfile = new()
        {
            WorkerProfileId = "profile-a",
            Image = new SandboxActivityImage
            {
                ImageRef = "example.com/repo/worker:latest",
            },
            Resources = new SandboxActivityResources
            {
                Cpu = "500m",
                Memory = "1024Mi",
            },
            MaxConcurrentActivities = 7,
        };
        workerProfile.Activities.Add(new SandboxActivity { Name = "RemoteHello" });

        // Act
        await transport.DeclareSandboxWorkerProfileAsync(workerProfile, TaskHub, CancellationToken.None);
        await using ISandboxActivityWorkerSession session = transport.OpenSandboxActivityWorkerSession(
            TaskHub,
            CancellationToken.None);

        // Assert
        callInvoker.WorkerProfileHeaders.Should().Contain(header => header.Key == "taskhub" && header.Value == TaskHub);
        callInvoker.WorkerSessionHeaders.Should().Contain(header => header.Key == "taskhub" && header.Value == TaskHub);
    }

    [Fact]
    public async Task SandboxActivitiesGrpcTransport_CanRelyOnChannelTaskHubMetadata()
    {
        // Arrange
        RecordingSandboxActivitiesCallInvoker callInvoker = new();
        SandboxActivitiesGrpcTransport transport = new(
            new SandboxActivities.SandboxActivitiesClient(callInvoker),
            attachTaskHubMetadata: false);
        SandboxWorkerProfile workerProfile = new()
        {
            WorkerProfileId = "profile-a",
            Image = new SandboxActivityImage
            {
                ImageRef = "example.com/repo/worker:latest",
            },
            Resources = new SandboxActivityResources
            {
                Cpu = "500m",
                Memory = "1024Mi",
            },
            MaxConcurrentActivities = 7,
        };
        workerProfile.Activities.Add(new SandboxActivity { Name = "RemoteHello" });

        // Act
        await transport.DeclareSandboxWorkerProfileAsync(workerProfile, TaskHub, CancellationToken.None);
        await using ISandboxActivityWorkerSession session = transport.OpenSandboxActivityWorkerSession(
            TaskHub,
            CancellationToken.None);

        // Assert
        callInvoker.WorkerProfileHeaders.Should().NotContain(header => header.Key == "taskhub");
        callInvoker.WorkerSessionHeaders.Should().NotContain(header => header.Key == "taskhub");
    }

    [Fact]
    public async Task SandboxActivityWorkerRegistrationHostedService_SendsLiveWorkerMetadataWithRegisteredActivities()
    {
        // Arrange
        string? originalSandboxProvider = Environment.GetEnvironmentVariable("DTS_SANDBOX_PROVIDER");
        string? originalSandboxId = Environment.GetEnvironmentVariable("DTS_SANDBOX_ID");
        Environment.SetEnvironmentVariable("DTS_SANDBOX_PROVIDER", "Sandbox");
        Environment.SetEnvironmentVariable("DTS_SANDBOX_ID", "sandbox-1");

        try
        {
            SandboxWorkerRuntimeOptions options = new()
            {
                TaskHub = TaskHub,
                WorkerProfileId = "profile-a",
                MaxConcurrentActivities = 3,
                HeartbeatInterval = TimeSpan.FromDays(1),
            };
            FakeSandboxActivitiesTransport client = new();
            SandboxActivityWorkerRegistrationHostedService service = new(
                client,
                options,
                Activities("RemoteHello"),
                NullLogger<SandboxActivityWorkerRegistrationHostedService>.Instance);

            // Act
            await service.StartAsync(CancellationToken.None);
            await client.Session.WaitForMessageAsync(message => message.Start != null);
            await service.StopAsync(CancellationToken.None);

            // Assert
            client.SessionTaskHubs.Should().Equal(TaskHub);
            SandboxActivityWorkerMessage message = client.Session.Messages.Should().ContainSingle().Subject;
            SandboxActivityWorkerStart start = message.Start;
            start.TaskHub.Should().Be(TaskHub);
            start.WorkerProfileId.Should().Be("profile-a");
            start.MaxActivitiesCount.Should().Be(3);
            start.SandboxProvider.Should().Be(SandboxProviderKind.Sandbox);
            start.DtsSandboxIdentifier.Should().Be("sandbox-1");
            start.Activities.Select(static activity => activity.Name).Should().Equal("RemoteHello");
            start.Activities.Select(static activity => activity.Version).Should().Equal(string.Empty);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DTS_SANDBOX_PROVIDER", originalSandboxProvider);
            Environment.SetEnvironmentVariable("DTS_SANDBOX_ID", originalSandboxId);
        }
    }

    [Fact]
    public void SandboxWorkerMessageBuilder_NormalizesTaskHubAndSandboxId()
    {
        // Arrange
        string? originalSandboxId = Environment.GetEnvironmentVariable("DTS_SANDBOX_ID");
        Environment.SetEnvironmentVariable("DTS_SANDBOX_ID", " sandbox-1 ");

        try
        {
            SandboxWorkerRuntimeOptions options = new()
            {
                TaskHub = " testhub ",
                WorkerProfileId = "profile-a",
                MaxConcurrentActivities = 3,
            };

            // Act
            SandboxActivityWorkerMessage message = SandboxWorkerMessageBuilder.BuildWorkerStart(
                options,
                Activities("RemoteHello"));

            // Assert
            SandboxActivityWorkerStart start = message.Start;
            start.TaskHub.Should().Be(TaskHub);
            start.DtsSandboxIdentifier.Should().Be("sandbox-1");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DTS_SANDBOX_ID", originalSandboxId);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SandboxWorkerMessageBuilder_MissingSandboxId_Throws(string? sandboxId)
    {
        // Arrange
        string? originalSandboxId = Environment.GetEnvironmentVariable("DTS_SANDBOX_ID");
        Environment.SetEnvironmentVariable("DTS_SANDBOX_ID", sandboxId);

        try
        {
            SandboxWorkerRuntimeOptions options = new()
            {
                TaskHub = TaskHub,
                WorkerProfileId = "profile-a",
                MaxConcurrentActivities = 3,
            };

            // Act
            Action action = () => SandboxWorkerMessageBuilder.BuildWorkerStart(
                options,
                Activities("RemoteHello"));

            // Assert
            action.Should().Throw<InvalidOperationException>()
                .WithMessage("On-demand sandbox activity worker registration requires a DTS sandbox ID.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DTS_SANDBOX_ID", originalSandboxId);
        }
    }

    [Fact]
    public void SandboxActivityTracker_TracksInFlightActivityCount()
    {
        // Arrange
        SandboxActivityTracker activityTracker = new();

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
    public async Task SandboxActivityWorkerRegistrationHostedService_SendsHeartbeatWithCurrentInFlightCount()
    {
        // Arrange
        using EnvironmentVariableScope sandboxId = new("DTS_SANDBOX_ID", "sandbox-1");
        SandboxWorkerRuntimeOptions options = new()
        {
            TaskHub = TaskHub,
            WorkerProfileId = "profile-a",
            MaxConcurrentActivities = 3,
            HeartbeatInterval = TimeSpan.FromMilliseconds(10),
        };

        FakeSandboxActivitiesTransport client = new();
        SandboxActivityTracker activityTracker = new();
        activityTracker.NotifyActivityStarted();
        activityTracker.NotifyActivityStarted();

        SandboxActivityWorkerRegistrationHostedService service = new(
            client,
            options,
            Activities("RemoteHello"),
            NullLogger<SandboxActivityWorkerRegistrationHostedService>.Instance,
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
    public async Task SandboxActivityWorkerRegistrationHostedService_ReopensSessionAfterTransientStreamFailure()
    {
        // Arrange
        using EnvironmentVariableScope sandboxId = new("DTS_SANDBOX_ID", "sandbox-1");
        SandboxWorkerRuntimeOptions options = new()
        {
            TaskHub = TaskHub,
            WorkerProfileId = "profile-a",
            MaxConcurrentActivities = 3,
            HeartbeatInterval = TimeSpan.FromMilliseconds(10),
            WorkerRegistrationRetryInitialDelay = TimeSpan.FromMilliseconds(10),
            WorkerRegistrationRetryMaxDelay = TimeSpan.FromMilliseconds(10),
        };

        FakeSandboxActivityWorkerSession failedSession = new() { ThrowOnWriteAttempt = 2 };
        FakeSandboxActivityWorkerSession recoveredSession = new();
        FakeSandboxActivitiesTransport client = new();
        client.QueueSession(failedSession);
        client.QueueSession(recoveredSession);

        SandboxActivityWorkerRegistrationHostedService service = new(
            client,
            options,
            Activities("RemoteHello"),
            NullLogger<SandboxActivityWorkerRegistrationHostedService>.Instance);

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
    public async Task SandboxActivityWorkerRegistrationHostedService_ReopensSessionAfterTerminalServerFailure()
    {
        // Arrange
        using EnvironmentVariableScope sandboxId = new("DTS_SANDBOX_ID", "sandbox-1");
        SandboxWorkerRuntimeOptions options = new()
        {
            TaskHub = $" {TaskHub} ",
            WorkerProfileId = "profile-a",
            MaxConcurrentActivities = 3,
            HeartbeatInterval = TimeSpan.FromDays(1),
            WorkerRegistrationRetryInitialDelay = TimeSpan.FromMilliseconds(10),
            WorkerRegistrationRetryMaxDelay = TimeSpan.FromMilliseconds(10),
        };

        FakeSandboxActivityWorkerSession failedSession = new();
        FakeSandboxActivityWorkerSession recoveredSession = new();
        FakeSandboxActivitiesTransport client = new();
        client.QueueSession(failedSession);
        client.QueueSession(recoveredSession);

        SandboxActivityWorkerRegistrationHostedService service = new(
            client,
            options,
            Activities("RemoteHello"),
            NullLogger<SandboxActivityWorkerRegistrationHostedService>.Instance);

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
    public async Task SandboxActivityWorkerRegistrationHostedService_DoesNotResetBackoffAfterStartMessageOnly()
    {
        // Arrange
        using EnvironmentVariableScope sandboxId = new("DTS_SANDBOX_ID", "sandbox-1");
        SandboxWorkerRuntimeOptions options = new()
        {
            TaskHub = TaskHub,
            WorkerProfileId = "profile-a",
            MaxConcurrentActivities = 3,
            HeartbeatInterval = TimeSpan.FromDays(1),
            WorkerRegistrationRetryInitialDelay = TimeSpan.FromMilliseconds(250),
            WorkerRegistrationRetryMaxDelay = TimeSpan.FromSeconds(1),
        };

        FakeSandboxActivityWorkerSession firstFailedSession = new();
        FakeSandboxActivityWorkerSession secondFailedSession = new();
        FakeSandboxActivityWorkerSession recoveredSession = new();
        FakeSandboxActivitiesTransport client = new();
        client.QueueSession(firstFailedSession);
        client.QueueSession(secondFailedSession);
        client.QueueSession(recoveredSession);

        SandboxActivityWorkerRegistrationHostedService service = new(
            client,
            options,
            Activities("RemoteHello"),
            NullLogger<SandboxActivityWorkerRegistrationHostedService>.Instance,
            reconnectJitter: new DeterministicRandom(0.999999));

        // Act
        await service.StartAsync(CancellationToken.None);
        await firstFailedSession.WaitForMessageAsync(message => message.Start != null);
        firstFailedSession.FailCompletion(new RpcException(new Status(StatusCode.Unavailable, "terminal-1")));
        await secondFailedSession.WaitForMessageAsync(message => message.Start != null);
        secondFailedSession.FailCompletion(new RpcException(new Status(StatusCode.Unavailable, "terminal-2")));
        Task recoveredStartTask = recoveredSession.WaitForMessageAsync(message => message.Start != null);
        Task completedTooEarly = await Task.WhenAny(
            recoveredStartTask,
            Task.Delay(TimeSpan.FromMilliseconds(375)));
        await recoveredStartTask.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        // Assert
        completedTooEarly.Should().NotBe(recoveredStartTask);
        client.SessionTaskHubs.Should().Equal(TaskHub, TaskHub, TaskHub);
    }

    [Fact]
    public void SandboxActivityWorkerRegistrationHostedService_ComputeJitteredReconnectDelay_UsesFullJitterWindow()
    {
        // Arrange
        TimeSpan retryDelay = TimeSpan.FromSeconds(10);

        // Act
        TimeSpan zero = SandboxActivityWorkerRegistrationHostedService.ComputeJitteredReconnectDelay(
            TimeSpan.Zero,
            new DeterministicRandom(0.5));
        TimeSpan low = SandboxActivityWorkerRegistrationHostedService.ComputeJitteredReconnectDelay(
            retryDelay,
            new DeterministicRandom(0.0));
        TimeSpan mid = SandboxActivityWorkerRegistrationHostedService.ComputeJitteredReconnectDelay(
            retryDelay,
            new DeterministicRandom(0.5));
        TimeSpan high = SandboxActivityWorkerRegistrationHostedService.ComputeJitteredReconnectDelay(
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
    public void SandboxActivityWorkerRegistrationHostedService_GetNextRetryDelay_SaturatesBeforeTickOverflow()
    {
        // Arrange
        // Act
        TimeSpan nextRetryDelay = SandboxActivityWorkerRegistrationHostedService.ComputeNextRetryDelay(
            TimeSpan.FromTicks((TimeSpan.MaxValue.Ticks / 2) + 1),
            TimeSpan.MaxValue);

        // Assert
        nextRetryDelay.Should().Be(TimeSpan.MaxValue);
    }

    [Fact]
    public async Task SandboxActivityWorkerRegistrationHostedService_AppliesJitterToReconnectDelay()
    {
        // Arrange
        using EnvironmentVariableScope sandboxId = new("DTS_SANDBOX_ID", "sandbox-1");
        SandboxWorkerRuntimeOptions options = new()
        {
            TaskHub = TaskHub,
            WorkerProfileId = "profile-a",
            MaxConcurrentActivities = 3,
            HeartbeatInterval = TimeSpan.FromMilliseconds(10),
            WorkerRegistrationRetryInitialDelay = TimeSpan.FromDays(1),
            WorkerRegistrationRetryMaxDelay = TimeSpan.FromDays(1),
        };

        FakeSandboxActivityWorkerSession failedSession = new() { ThrowOnWriteAttempt = 2 };
        FakeSandboxActivityWorkerSession recoveredSession = new();
        FakeSandboxActivitiesTransport client = new();
        client.QueueSession(failedSession);
        client.QueueSession(recoveredSession);

        SandboxActivityWorkerRegistrationHostedService service = new(
            client,
            options,
            Activities("RemoteHello"),
            NullLogger<SandboxActivityWorkerRegistrationHostedService>.Instance,
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
    public async Task SandboxActivityWorkerRegistrationHostedService_StopAsync_DoesNotCompleteStreamWhileWriteIsInFlight()
    {
        // Arrange
        using EnvironmentVariableScope sandboxId = new("DTS_SANDBOX_ID", "sandbox-1");
        SandboxWorkerRuntimeOptions options = new()
        {
            TaskHub = TaskHub,
            WorkerProfileId = "profile-a",
            MaxConcurrentActivities = 3,
            HeartbeatInterval = TimeSpan.FromMilliseconds(10),
        };

        FakeSandboxActivityWorkerSession session = new() { BlockWriteAttempt = 2 };
        FakeSandboxActivitiesTransport client = new();
        client.QueueSession(session);

        SandboxActivityWorkerRegistrationHostedService service = new(
            client,
            options,
            Activities("RemoteHello"),
            NullLogger<SandboxActivityWorkerRegistrationHostedService>.Instance);

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
    public async Task SandboxActivityWorkerRegistrationHostedService_StopAsync_DisposesSessionAfterCompletion()
    {
        // Arrange
        using EnvironmentVariableScope sandboxId = new("DTS_SANDBOX_ID", "sandbox-1");
        SandboxWorkerRuntimeOptions options = new()
        {
            TaskHub = TaskHub,
            WorkerProfileId = "profile-a",
            MaxConcurrentActivities = 3,
            HeartbeatInterval = TimeSpan.FromMilliseconds(10),
        };

        FakeSandboxActivityWorkerSession session = new() { BlockComplete = true };
        FakeSandboxActivitiesTransport client = new();
        client.QueueSession(session);

        SandboxActivityWorkerRegistrationHostedService service = new(
            client,
            options,
            Activities("RemoteHello"),
            NullLogger<SandboxActivityWorkerRegistrationHostedService>.Instance);

        // Act
        await service.StartAsync(CancellationToken.None);
        await session.WaitForMessageAsync(message => message.Start != null);
        Task stopTask = service.StopAsync(CancellationToken.None);
        await session.WaitForBlockedCompleteAsync();
        Task disposeDelay = Task.Delay(TimeSpan.FromMilliseconds(100));
        Task disposeBeforeCompleteReleased = await Task.WhenAny(
            session.WaitForDisposeAsync(),
            disposeDelay);
        session.ReleaseBlockedComplete();
        await stopTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        disposeBeforeCompleteReleased.Should().Be(disposeDelay);
        session.CompleteCalled.Should().BeTrue();
        session.DisposeCalled.Should().BeTrue();
        session.DisposeCalledWhileCompleteActive.Should().BeFalse();
    }

    [Fact]
    public async Task UseSandboxWorker_ConfiguresRegisteredActivityWorkerFilter()
    {
        // Arrange
        using EnvironmentVariableScope endpoint = new("DTS_ENDPOINT", "https://example.scheduler");
        using EnvironmentVariableScope taskHub = new("DTS_TASK_HUB", TaskHub);
        using EnvironmentVariableScope workerProfile = new("DTS_WORKER_PROFILE_ID", "profile-a");
        using EnvironmentVariableScope auth = new("DTS_AUTHENTICATION", "ManagedIdentity");
        using EnvironmentVariableScope clientId = new("DTS_UMI_CLIENT_ID", "worker-client-id");
        using EnvironmentVariableScope sandboxProvider = new("DTS_SANDBOX_PROVIDER", "Sandbox");
        using EnvironmentVariableScope maxActivities = new("DTS_SANDBOX_MAX_ACTIVITIES", "3");
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

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("many")]
    public async Task UseSandboxWorker_InvalidMaxActivities_ThrowsWhenWorkerOptionsAreResolved(string maxActivitiesValue)
    {
        // Arrange
        using EnvironmentVariableScope endpoint = new("DTS_ENDPOINT", "https://example.scheduler");
        using EnvironmentVariableScope taskHub = new("DTS_TASK_HUB", TaskHub);
        using EnvironmentVariableScope workerProfile = new("DTS_WORKER_PROFILE_ID", "profile-a");
        using EnvironmentVariableScope auth = new("DTS_AUTHENTICATION", "ManagedIdentity");
        using EnvironmentVariableScope clientId = new("DTS_UMI_CLIENT_ID", "worker-client-id");
        using EnvironmentVariableScope sandboxProvider = new("DTS_SANDBOX_PROVIDER", "Sandbox");
        using EnvironmentVariableScope maxActivities = new("DTS_SANDBOX_MAX_ACTIVITIES", maxActivitiesValue);
        ServiceCollection services = new();
        services.Configure<DurableTaskRegistry>(
            Options.DefaultName,
            registry => registry.AddActivityFunc<string, string>(new TaskName("RemoteHello"), (_, input) => input));
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        mockBuilder.Object.UseSandboxWorker();
        await using ServiceProvider provider = services.BuildServiceProvider();

        // Act
        Action action = () => provider
            .GetRequiredService<IOptionsMonitor<DurableTaskWorkerOptions>>()
            .Get(Options.DefaultName);

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("DTS_SANDBOX_MAX_ACTIVITIES must be a positive integer when injected by DTS for on-demand sandbox workers.");
    }

    [Fact]
    public async Task UseSandboxWorker_WithNoRegisteredActivities_FailsWhenWorkerFiltersAreResolved()
    {
        // Arrange
        using EnvironmentVariableScope endpoint = new("DTS_ENDPOINT", "https://example.scheduler");
        using EnvironmentVariableScope taskHub = new("DTS_TASK_HUB", TaskHub);
        using EnvironmentVariableScope workerProfile = new("DTS_WORKER_PROFILE_ID", "profile-a");
        using EnvironmentVariableScope auth = new("DTS_AUTHENTICATION", "ManagedIdentity");
        using EnvironmentVariableScope clientId = new("DTS_UMI_CLIENT_ID", "worker-client-id");
        using EnvironmentVariableScope sandboxProvider = new("DTS_SANDBOX_PROVIDER", "Sandbox");
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
    public async Task UseSandboxWorker_ConfiguresSchedulerWithManagedIdentityCredential()
    {
        // Arrange
        using EnvironmentVariableScope endpoint = new("DTS_ENDPOINT", "https://example.scheduler");
        using EnvironmentVariableScope taskHub = new("DTS_TASK_HUB", TaskHub);
        using EnvironmentVariableScope workerProfile = new("DTS_WORKER_PROFILE_ID", "profile-a");
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
    public void UseSandboxWorker_MissingAuthentication_Throws()
    {
        // Arrange
        using EnvironmentVariableScope endpoint = new("DTS_ENDPOINT", "https://example.scheduler");
        using EnvironmentVariableScope taskHub = new("DTS_TASK_HUB", TaskHub);
        using EnvironmentVariableScope workerProfile = new("DTS_WORKER_PROFILE_ID", "profile-a");
        using EnvironmentVariableScope auth = new("DTS_AUTHENTICATION", null);
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        // Act
        Action action = () => mockBuilder.Object.UseSandboxWorker();

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("DTS_AUTHENTICATION must be injected by DTS for on-demand sandbox workers.");
    }

    [Fact]
    public async Task UseSandboxWorker_InvalidAuthentication_ThrowsWhenSchedulerOptionsAreResolved()
    {
        // Arrange
        using EnvironmentVariableScope endpoint = new("DTS_ENDPOINT", "https://example.scheduler");
        using EnvironmentVariableScope taskHub = new("DTS_TASK_HUB", TaskHub);
        using EnvironmentVariableScope workerProfile = new("DTS_WORKER_PROFILE_ID", "profile-a");
        using EnvironmentVariableScope auth = new("DTS_AUTHENTICATION", "ManagedIdentty");
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        mockBuilder.Object.UseSandboxWorker();
        await using ServiceProvider provider = services.BuildServiceProvider();

        // Act
        Action action = () => provider
            .GetRequiredService<IOptionsMonitor<DurableTaskSchedulerWorkerOptions>>()
            .Get(Options.DefaultName);

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("DTS_AUTHENTICATION must be 'ManagedIdentity' for on-demand sandbox workers.");
    }

    [Fact]
    public async Task UseSandboxWorker_WithManagedIdentityAuth_ConfiguresSchedulerCredential()
    {
        // Arrange
        using EnvironmentVariableScope endpoint = new("DTS_ENDPOINT", "https://example.scheduler");
        using EnvironmentVariableScope taskHub = new("DTS_TASK_HUB", TaskHub);
        using EnvironmentVariableScope workerProfile = new("DTS_WORKER_PROFILE_ID", "profile-a");
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
        using EnvironmentVariableScope workerProfile = new("DTS_WORKER_PROFILE_ID", "profile-a");
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
        using EnvironmentVariableScope workerProfile = new("DTS_WORKER_PROFILE_ID", "profile-a");
        using EnvironmentVariableScope auth = new("DTS_AUTHENTICATION", "ManagedIdentity");
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
        using EnvironmentVariableScope workerProfile = new("DTS_WORKER_PROFILE_ID", "profile-a");
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

    [Fact]
    public async Task UseSandboxWorker_MissingInjectedSandboxProvider_ThrowsWhenWorkerOptionsAreResolved()
    {
        // Arrange
        using EnvironmentVariableScope endpoint = new("DTS_ENDPOINT", "https://example.scheduler");
        using EnvironmentVariableScope taskHub = new("DTS_TASK_HUB", TaskHub);
        using EnvironmentVariableScope workerProfile = new("DTS_WORKER_PROFILE_ID", "profile-a");
        using EnvironmentVariableScope auth = new("DTS_AUTHENTICATION", "ManagedIdentity");
        using EnvironmentVariableScope clientId = new("DTS_UMI_CLIENT_ID", "worker-client-id");
        using EnvironmentVariableScope sandboxProvider = new("DTS_SANDBOX_PROVIDER", null);
        ServiceCollection services = new();
        services.Configure<DurableTaskRegistry>(
            Options.DefaultName,
            registry => registry.AddActivityFunc<string, string>(new TaskName("RemoteHello"), (_, input) => input));
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        mockBuilder.Object.UseSandboxWorker();
        await using ServiceProvider provider = services.BuildServiceProvider();

        // Act
        Action action = () => provider
            .GetRequiredService<IOptionsMonitor<DurableTaskWorkerOptions>>()
            .Get(Options.DefaultName);

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("DTS_SANDBOX_PROVIDER must be injected by DTS for on-demand sandbox workers.");
    }

    [Fact]
    public async Task UseSandboxWorker_InvalidInjectedSandboxProvider_ThrowsWhenWorkerOptionsAreResolved()
    {
        // Arrange
        using EnvironmentVariableScope endpoint = new("DTS_ENDPOINT", "https://example.scheduler");
        using EnvironmentVariableScope taskHub = new("DTS_TASK_HUB", TaskHub);
        using EnvironmentVariableScope workerProfile = new("DTS_WORKER_PROFILE_ID", "profile-a");
        using EnvironmentVariableScope auth = new("DTS_AUTHENTICATION", "ManagedIdentity");
        using EnvironmentVariableScope clientId = new("DTS_UMI_CLIENT_ID", "worker-client-id");
        using EnvironmentVariableScope sandboxProvider = new("DTS_SANDBOX_PROVIDER", "ContainerApp");
        ServiceCollection services = new();
        services.Configure<DurableTaskRegistry>(
            Options.DefaultName,
            registry => registry.AddActivityFunc<string, string>(new TaskName("RemoteHello"), (_, input) => input));
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        mockBuilder.Object.UseSandboxWorker();
        await using ServiceProvider provider = services.BuildServiceProvider();

        // Act
        Action action = () => provider
            .GetRequiredService<IOptionsMonitor<DurableTaskWorkerOptions>>()
            .Get(Options.DefaultName);

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("DTS_SANDBOX_PROVIDER must be 'Sandbox' or 'AcaSessionPool' for on-demand sandbox workers.");
    }

    static IReadOnlyCollection<SandboxActivityMetadata.Activity> Activities(params string[] names) =>
        names.Select(static name => new SandboxActivityMetadata.Activity(name, Version: null)).ToArray();

    sealed class FakeSandboxActivitiesTransport : ISandboxActivitiesTransport
    {
        readonly Queue<FakeSandboxActivityWorkerSession> queuedSessions = new();

        public List<string> SessionTaskHubs { get; } = [];

        public List<FakeSandboxActivityWorkerSession> Sessions { get; } = [];

        public FakeSandboxActivityWorkerSession Session { get; } = new();

        public void QueueSession(FakeSandboxActivityWorkerSession session) => this.queuedSessions.Enqueue(session);

        public Task<DeclareSandboxWorkerProfileResult> DeclareSandboxWorkerProfileAsync(
            SandboxWorkerProfile workerProfile,
            string taskHub,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<RemoveSandboxWorkerProfileResult> RemoveSandboxWorkerProfileAsync(
            string workerProfileId,
            string taskHub,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public ISandboxActivityWorkerSession OpenSandboxActivityWorkerSession(string taskHub, CancellationToken cancellationToken)
        {
            this.SessionTaskHubs.Add(taskHub);
            FakeSandboxActivityWorkerSession session = this.queuedSessions.Count > 0
                ? this.queuedSessions.Dequeue()
                : this.Session;
            this.Sessions.Add(session);
            return session;
        }
    }

    sealed class RecordingSandboxActivitiesCallInvoker : CallInvoker
    {
        public Metadata WorkerProfileHeaders { get; private set; } = [];

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
            method.FullName.Should().EndWith("/DeclareSandboxWorkerProfile");
            this.WorkerProfileHeaders = options.Headers ?? [];

            return new AsyncUnaryCall<TResponse>(
                Task.FromResult((TResponse)(object)new DeclareSandboxWorkerProfileResult()),
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
            method.FullName.Should().EndWith("/ConnectSandboxActivityWorker");
            this.WorkerSessionHeaders = options.Headers ?? [];

            return new AsyncClientStreamingCall<TRequest, TResponse>(
                new RecordingClientStreamWriter<TRequest>(),
                Task.FromResult((TResponse)(object)new SandboxActivityWorkerSessionResult()),
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

    sealed class FakeSandboxActivityWorkerSession : ISandboxActivityWorkerSession
    {
        readonly object sync = new();
        readonly TaskCompletionSource<SandboxActivityWorkerSessionResult> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource blockedWriteStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource releaseBlockedWrite =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource blockedCompleteStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource releaseBlockedComplete =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        int writeAttempts;
        int activeWrites;
        int activeCompletes;

        public List<SandboxActivityWorkerMessage> Messages { get; } = [];

        public int? ThrowOnWriteAttempt { get; init; }

        public int? BlockWriteAttempt { get; init; }

        public bool BlockComplete { get; init; }

        public bool CompleteCalled { get; private set; }

        public bool CompleteCalledWhileWriteActive { get; private set; }

        public bool DisposeCalled { get; private set; }

        public bool DisposeCalledWhileCompleteActive { get; private set; }

        public void FailCompletion(Exception exception) => this.completion.TrySetException(exception);

        public Task WaitForBlockedWriteAsync() => this.blockedWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public Task WaitForBlockedCompleteAsync() => this.blockedCompleteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public Task WaitForDisposeAsync() => this.disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public Task WaitForCompleteAsync()
        {
            lock (this.sync)
            {
                return this.CompleteCalled ? Task.CompletedTask : this.completion.Task;
            }
        }

        public void ReleaseBlockedWrite() => this.releaseBlockedWrite.TrySetResult();

        public void ReleaseBlockedComplete() => this.releaseBlockedComplete.TrySetResult();

        public async Task WaitForMessageAsync(Func<SandboxActivityWorkerMessage, bool> predicate)
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

                await Task.Delay(TimeSpan.FromMilliseconds(10));
            }

            throw new TimeoutException("Timed out waiting for on-demand sandbox worker message.");
        }

        public Task WriteMessageAsync(SandboxActivityWorkerMessage message)
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

        public Task<SandboxActivityWorkerSessionResult> WaitForCompletionAsync() => this.completion.Task;

        public async Task CompleteAsync()
        {
            bool blockComplete;
            lock (this.sync)
            {
                this.CompleteCalled = true;
                this.CompleteCalledWhileWriteActive = this.activeWrites > 0;
                this.activeCompletes++;
                blockComplete = this.BlockComplete;
                if (blockComplete)
                {
                    this.blockedCompleteStarted.TrySetResult();
                }
            }

            try
            {
                if (blockComplete)
                {
                    await this.releaseBlockedComplete.Task.ConfigureAwait(false);
                }

                this.completion.TrySetResult(new SandboxActivityWorkerSessionResult());
                await this.completion.Task.ConfigureAwait(false);
            }
            finally
            {
                lock (this.sync)
                {
                    this.activeCompletes--;
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (this.sync)
            {
                this.DisposeCalled = true;
                this.DisposeCalledWhileCompleteActive = this.activeCompletes > 0;
                this.disposed.TrySetResult();
            }

            return default;
        }

        async Task WriteMessageCoreAsync(SandboxActivityWorkerMessage message, bool blockWrite)
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
