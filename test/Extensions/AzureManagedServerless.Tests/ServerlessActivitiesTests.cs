// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Net.Sockets;
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
        typeof(ServerlessActivityDeclaration).GetProperty("LaunchCommand").Should().BeNull();
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
        declaration.MaxConcurrentActivities.Should().Be(7);
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
        options.ActivityNames.Add("RemoteHello");
        FakeServerlessActivitiesClient client = new();
        ServerlessActivityDeclarationHostedService service = new(
            client,
            options,
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
        options.ActivityNames.Add("RemoteHello");
        FakeServerlessActivitiesClient client = new() { TransientDeclarationFailures = 1 };
        ServerlessActivityDeclarationHostedService service = new(
            client,
            options,
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
            start.SandboxId.Should().Be("sandbox-1");
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
        ServerlessOptions options = new()
        {
            Mode = ServerlessMode.ServerlessInclude,
            TaskHub = TaskHub,
            WorkerProfileId = "profile-a",
            MaxConcurrentActivities = 3,
            HeartbeatInterval = TimeSpan.FromMilliseconds(10),
        };
        options.ActivityNames.Add("RemoteHello");

        FakeServerlessActivitiesClient client = new();
        ServerlessActivityTracker activityTracker = new();
        activityTracker.NotifyActivityStarted();
        activityTracker.NotifyActivityStarted();

        ServerlessActivityWorkerRegistrationHostedService service = new(
            client,
            options,
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
        ServerlessOptions options = new()
        {
            Mode = ServerlessMode.ServerlessInclude,
            TaskHub = TaskHub,
            WorkerProfileId = "profile-a",
            MaxConcurrentActivities = 3,
            HeartbeatInterval = TimeSpan.FromMilliseconds(10),
            WorkerRegistrationRetryInitialDelay = TimeSpan.FromMilliseconds(10),
            WorkerRegistrationRetryMaxDelay = TimeSpan.FromMilliseconds(10),
        };
        options.ActivityNames.Add("RemoteHello");

        FakeServerlessActivityWorkerSession failedSession = new() { ThrowOnWriteAttempt = 2 };
        FakeServerlessActivityWorkerSession recoveredSession = new();
        FakeServerlessActivitiesClient client = new();
        client.QueueSession(failedSession);
        client.QueueSession(recoveredSession);

        ServerlessActivityWorkerRegistrationHostedService service = new(
            client,
            options,
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
        ServerlessOptions options = new()
        {
            Mode = ServerlessMode.ServerlessInclude,
            TaskHub = TaskHub,
            WorkerProfileId = "profile-a",
            MaxConcurrentActivities = 3,
            HeartbeatInterval = TimeSpan.FromDays(1),
            WorkerRegistrationRetryInitialDelay = TimeSpan.FromMilliseconds(10),
            WorkerRegistrationRetryMaxDelay = TimeSpan.FromMilliseconds(10),
        };
        options.ActivityNames.Add("RemoteHello");

        FakeServerlessActivityWorkerSession failedSession = new();
        FakeServerlessActivityWorkerSession recoveredSession = new();
        FakeServerlessActivitiesClient client = new();
        client.QueueSession(failedSession);
        client.QueueSession(recoveredSession);

        ServerlessActivityWorkerRegistrationHostedService service = new(
            client,
            options,
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
        ServerlessOptions options = new()
        {
            Mode = ServerlessMode.ServerlessInclude,
            TaskHub = TaskHub,
            WorkerProfileId = "profile-a",
            MaxConcurrentActivities = 3,
            HeartbeatInterval = TimeSpan.FromMilliseconds(10),
            WorkerRegistrationRetryInitialDelay = TimeSpan.FromDays(1),
            WorkerRegistrationRetryMaxDelay = TimeSpan.FromDays(1),
        };
        options.ActivityNames.Add("RemoteHello");

        FakeServerlessActivityWorkerSession failedSession = new() { ThrowOnWriteAttempt = 2 };
        FakeServerlessActivityWorkerSession recoveredSession = new();
        FakeServerlessActivitiesClient client = new();
        client.QueueSession(failedSession);
        client.QueueSession(recoveredSession);

        ServerlessActivityWorkerRegistrationHostedService service = new(
            client,
            options,
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
        ServerlessOptions options = new()
        {
            Mode = ServerlessMode.ServerlessInclude,
            TaskHub = TaskHub,
            WorkerProfileId = "profile-a",
            MaxConcurrentActivities = 3,
            HeartbeatInterval = TimeSpan.FromMilliseconds(10),
        };
        options.ActivityNames.Add("RemoteHello");

        FakeServerlessActivityWorkerSession session = new() { BlockWriteAttempt = 2 };
        FakeServerlessActivitiesClient client = new();
        client.QueueSession(session);

        ServerlessActivityWorkerRegistrationHostedService service = new(
            client,
            options,
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
    public async Task DeclareServerlessActivities_ConfiguresLocalWorkerExclusionFilter()
    {
        // Arrange
        using EnvironmentVariableScope serverlessActivities = new("DTS_SERVERLESS_ACTIVITIES", null);
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        // Act
        mockBuilder.Object.DeclareServerlessActivities(options =>
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
    public async Task DeclareServerlessActivities_DoesNotConfigureFilterWhenActivityNamesAreEmpty()
    {
        // Arrange
        using EnvironmentVariableScope serverlessActivities = new("DTS_SERVERLESS_ACTIVITIES", null);
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        // Act
        mockBuilder.Object.DeclareServerlessActivities(options =>
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
    public async Task UseServerlessWorker_ConfiguresServerlessActivityWorkerFilter()
    {
        // Arrange
        using EnvironmentVariableScope serverlessActivities = new("DTS_SERVERLESS_ACTIVITIES", "RemoteHello");
        ServiceCollection services = new();
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
    public void UseServerlessWorker_RegistersWakeupServerHostedService()
    {
        // Arrange
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(builder => builder.Services).Returns(services);
        mockBuilder.Setup(builder => builder.Name).Returns(Options.DefaultName);

        // Act
        mockBuilder.Object.UseServerlessWorker();

        // Assert
        services.Count(descriptor => descriptor.ServiceType == typeof(IHostedService)).Should().Be(2);
    }

    [Fact]
    public async Task ServerlessWakeupServer_RespondsToAdcProbesWhenWorkerIsServerless()
    {
        // Arrange
        int wakeupPort = GetFreeTcpPort();
        ServerlessOptions options = new()
        {
            Mode = ServerlessMode.ServerlessInclude,
            WakeupPort = wakeupPort,
        };
        ServerlessWakeupServer server = new(
            options,
            NullLogger<ServerlessWakeupServer>.Instance);

        // Act
        await server.StartAsync(CancellationToken.None);

        try
        {
            using HttpClient httpClient = new();

            // Assert
            using HttpResponseMessage healthResponse = await httpClient.GetAsync(
                $"http://127.0.0.1:{wakeupPort}/health");
            healthResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            using HttpResponseMessage wakeupResponse = await httpClient.PostAsync(
                $"http://127.0.0.1:{wakeupPort}/wakeup",
                new ByteArrayContent([]));
            wakeupResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
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

    static int GetFreeTcpPort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
