// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Grpc.Core;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Grpc;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests.DependencyInjection;

public class UseExternalizedPayloadsTests
{
    [Fact]
    public void UseExternalizedPayloads_Client_RegistersNoHostedService()
    {
        // Arrange - auto-purge is now an explicit client call, not a declarative option, so nothing about this
        // extension may start background work. A hosted service here would reintroduce the behaviour the
        // redesign removed: a host that reasserts a task-hub-wide setting simply because it booted.
        ServiceCollection services = new();
        Mock<IDurableTaskClientBuilder> builder = new();
        builder.Setup(b => b.Services).Returns(services);
        builder.Setup(b => b.Name).Returns(string.Empty);

        // Act
        builder.Object.UseExternalizedPayloads(options => options.ConnectionString = "UseDevelopmentStorage=true");

        // Assert
        services.Should().NotContain(d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void UseExternalizedPayloads_Worker_RegistersNoHostedService()
    {
        // Arrange - the worker mirror. A worker never announces the auto-purge setting either; it only runs the
        // job's orchestrator and activities when something else has started the job.
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> builder = new();
        builder.Setup(b => b.Services).Returns(services);
        builder.Setup(b => b.Name).Returns(string.Empty);
        services.AddOptions<GrpcDurableTaskWorkerOptions>(string.Empty)
            .Configure(o => o.CallInvoker = Mock.Of<CallInvoker>());

        // Act
        builder.Object.UseExternalizedPayloads(options => options.ConnectionString = "UseDevelopmentStorage=true");

        // Assert
        services.Should().NotContain(d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void UseExternalizedPayloads_ConfigureDelegate_InvokedExactlyOnce()
    {
        // Arrange - a delegate that counts its invocations. The old probe-at-registration ran configure a second
        // time against a throwaway options instance; this locks in that user code runs exactly once, when the
        // named options are first materialized.
        ServiceCollection services = new();
        Mock<IDurableTaskClientBuilder> builder = new();
        builder.Setup(b => b.Services).Returns(services);
        builder.Setup(b => b.Name).Returns(string.Empty);

        int invocations = 0;

        // Act
        builder.Object.UseExternalizedPayloads(options => invocations++);
        using ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptionsMonitor<LargePayloadStorageOptions>>().Get(string.Empty);

        // Assert - configure ran once (at options materialization), not a second time at registration.
        invocations.Should().Be(1);
    }

    [Fact]
    public void UseExternalizedPayloads_ClientOnly_RegistersResolvablePayloadStore()
    {
        // Arrange - a client-only host with no worker and no explicit AddExternalizedPayloadStore. This is the
        // exact shape that previously failed: the core method declared a PostConfigure dependency on
        // PayloadStore without ever registering it, so options resolution threw at runtime.
        ServiceCollection services = new();
        Mock<IDurableTaskClientBuilder> builder = new();
        builder.Setup(b => b.Services).Returns(services);
        builder.Setup(b => b.Name).Returns(string.Empty);

        // Act - UseDevelopmentStorage=true is a valid connection string that BlobServiceClient accepts with no
        // network I/O, so the store constructs offline. Build the provider and actually resolve PayloadStore.
        builder.Object.UseExternalizedPayloads(options => options.ConnectionString = "UseDevelopmentStorage=true");
        using ServiceProvider clientProvider = services.BuildServiceProvider();

        // Assert - the store resolves without throwing and is the blob-backed implementation.
        PayloadStore store = clientProvider.GetRequiredService<PayloadStore>();
        store.Should().BeOfType<BlobPayloadStore>();
    }

    [Fact]
    public void UseExternalizedPayloads_Worker_LeavesTheGrpcWorkerOptionsAlone()
    {
        // Arrange - the worker used to carry a resolved auto-purge flag that it announced once per connection.
        // That is gone: the setting belongs to the task hub and is written by an explicit client call, so this
        // extension must not add a worker-scoped copy of it. What the worker DOES configure is the transport the
        // purge activities ride, which PurgeTransportTests covers.
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> builder = new();
        builder.Setup(b => b.Services).Returns(services);
        builder.Setup(b => b.Name).Returns(string.Empty);
        CallInvoker configured = Mock.Of<CallInvoker>();
        services.AddOptions<GrpcDurableTaskWorkerOptions>(string.Empty)
            .Configure(o => o.CallInvoker = configured);

        // Act
        builder.Object.UseExternalizedPayloads(options => options.ConnectionString = "UseDevelopmentStorage=true");

        using ServiceProvider provider = services.BuildServiceProvider();
        GrpcDurableTaskWorkerOptions grpcOptions =
            provider.GetRequiredService<IOptionsMonitor<GrpcDurableTaskWorkerOptions>>().Get(string.Empty);

        // Assert - the configured transport survives PostConfigure untouched, and the large-payload capability
        // is the only thing this extension adds to the worker's gRPC options.
        grpcOptions.CallInvoker.Should().BeSameAs(configured);
        grpcOptions.Channel.Should().BeNull();
    }

    [Fact]
    public void UseExternalizedPayloads_Client_EnablesEntitySupport()
    {
        // Arrange - the public SetLargePayloadAutoPurgeAsync extension reaches the singleton job through
        // client.Entities on BOTH the enable and disable paths, so the client must turn entity support on
        // whenever externalized payloads are configured. Nothing gates this on whether auto-purge is on: the
        // client cannot know that, and reading client.Entities is what the API does before it touches the
        // backend.
        ServiceCollection services = new();
        Mock<IDurableTaskClientBuilder> builder = new();
        builder.Setup(b => b.Services).Returns(services);
        builder.Setup(b => b.Name).Returns(string.Empty);

        // Act
        builder.Object.UseExternalizedPayloads(options => { });
        using ServiceProvider provider = services.BuildServiceProvider();
        DurableTaskClientOptions options =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskClientOptions>>().Get(string.Empty);

        // Assert
        options.EnableEntitySupport.Should().BeTrue();
    }

    [Fact]
    public void UseExternalizedPayloads_Worker_EnablesEntitySupport()
    {
        // Arrange - the purge orchestrator drives the BlobPurgeJob entity, and an orchestrator that touches
        // entities with support off throws, so the worker must turn entity support on whenever externalized
        // payloads are configured.
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> builder = new();
        builder.Setup(b => b.Services).Returns(services);
        builder.Setup(b => b.Name).Returns(string.Empty);

        // Act
        builder.Object.UseExternalizedPayloads(options => { });
        using ServiceProvider provider = services.BuildServiceProvider();
        DurableTaskWorkerOptions options =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerOptions>>().Get(string.Empty);

        // Assert
        options.EnableEntitySupport.Should().BeTrue();
    }

    [Fact]
    public void UseExternalizedPayloads_WorkerOnly_PurgeActivitiesAreConstructible()
    {
        // Arrange - a WORKER-ONLY host: AddDurableTaskWorker + UseExternalizedPayloads, with no AddDurableTaskClient
        // anywhere. This is the split-deployment shape ("client triggers, worker executes"). The purge activities
        // used to inject a concrete DurableTaskClient, which a worker-only host never registers, so they threw at
        // dispatch time - and only at dispatch, because DurableTaskRegistry stores a lazy
        // ActivatorUtilities.GetServiceOrCreateInstance factory - leaving auto-purge silently broken. They now
        // inject the worker's own LargePayloadPurge client, which rides the transport the worker publishes at
        // runtime, so they construct with no DurableTaskClient present. UseGrpc is here because a real worker
        // configures a transport, not because resolving the purge client needs one - see
        // PurgeTransportTests.AddressOnlyWorker_ResolvesThePurgeClient for the configuration that has neither
        // Channel nor CallInvoker.
        ServiceCollection services = new();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddDurableTaskWorker(builder =>
        {
            builder.UseGrpc(options => options.CallInvoker = Mock.Of<CallInvoker>());
            builder.UseExternalizedPayloads(options => options.ConnectionString = "UseDevelopmentStorage=true");
        });

        using ServiceProvider provider = services.BuildServiceProvider();

        // Act - construct both activities through the exact reflection path DurableTaskRegistry uses at dispatch.
        Action constructGet = () =>
            ActivatorUtilities.GetServiceOrCreateInstance(provider, typeof(GetLargePayloadTombstonesActivity));
        Action constructReport = () =>
            ActivatorUtilities.GetServiceOrCreateInstance(provider, typeof(ReportLargePayloadPurgeResultsActivity));

        // Assert - both must resolve without a client in the container. This throws on the pre-fix code.
        constructGet.Should().NotThrow();
        constructReport.Should().NotThrow();
    }

    [Fact]
    public void UseExternalizedPayloads_NamedClientBuilder_ResolvesEverythingUnderThatName()
    {
        // Arrange - every other test in this file drives builder.Name == string.Empty, so the entire named code
        // path had no coverage: the storage options, the client's entity-support flip and the intercepted gRPC
        // client options are ALL keyed on builder.Name. Drive a non-empty name end to end.
        const string name = "client-hub";
        ServiceCollection services = new();
        services.AddSingleton(Mock.Of<IDurableTaskClientProvider>());
        services.AddOptions<GrpcDurableTaskClientOptions>(name)
            .Configure(o => o.CallInvoker = Mock.Of<CallInvoker>());

        Mock<IDurableTaskClientBuilder> builder = new();
        builder.Setup(b => b.Services).Returns(services);
        builder.Setup(b => b.Name).Returns(name);

        // Act
        builder.Object.UseExternalizedPayloads(
            options => options.ConnectionString = "UseDevelopmentStorage=true");
        using ServiceProvider provider = services.BuildServiceProvider();

        // Assert - the storage options, the entity-support flip and the intercepted gRPC client options all
        // materialize under the builder name, and the store resolves from the built provider.
        provider.GetRequiredService<IOptionsMonitor<LargePayloadStorageOptions>>().Get(name)
            .ConnectionString.Should().Be("UseDevelopmentStorage=true");
        provider.GetRequiredService<IOptionsMonitor<DurableTaskClientOptions>>().Get(name)
            .EnableEntitySupport.Should().BeTrue();
        provider.GetRequiredService<IOptionsMonitor<GrpcDurableTaskClientOptions>>().Get(name)
            .CallInvoker.Should().NotBeNull();
        provider.GetRequiredService<PayloadStore>().Should().BeOfType<BlobPayloadStore>();
    }

    [Fact]
    public void UseExternalizedPayloads_NamedWorkerBuilder_ResolvesEverythingUnderThatName()
    {
        // Arrange - the worker mirror of the named-client gap: the storage options, the worker's entity-support
        // flip and the purge transport the activities inject are all keyed on builder.Name, and every existing
        // worker test uses the empty name. Drive a non-empty name.
        const string name = "worker-hub";
        ServiceCollection services = new();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddOptions<GrpcDurableTaskWorkerOptions>(name)
            .Configure(o => o.CallInvoker = Mock.Of<CallInvoker>());

        Mock<IDurableTaskWorkerBuilder> builder = new();
        builder.Setup(b => b.Services).Returns(services);
        builder.Setup(b => b.Name).Returns(name);

        // Act
        builder.Object.UseExternalizedPayloads(
            options => options.ConnectionString = "UseDevelopmentStorage=true");
        using ServiceProvider provider = services.BuildServiceProvider();

        // Assert - both option sets resolve under the name, the store is blob-backed, and both purge activities
        // construct through the exact reflection path DurableTaskRegistry uses at dispatch.
        provider.GetRequiredService<IOptionsMonitor<LargePayloadStorageOptions>>().Get(name)
            .ConnectionString.Should().Be("UseDevelopmentStorage=true");
        provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerOptions>>().Get(name)
            .EnableEntitySupport.Should().BeTrue();
        provider.GetRequiredService<PayloadStore>().Should().BeOfType<BlobPayloadStore>();

        Action constructGet = () =>
            ActivatorUtilities.GetServiceOrCreateInstance(provider, typeof(GetLargePayloadTombstonesActivity));
        Action constructReport = () =>
            ActivatorUtilities.GetServiceOrCreateInstance(provider, typeof(ReportLargePayloadPurgeResultsActivity));
        constructGet.Should().NotThrow();
        constructReport.Should().NotThrow();
    }

    [Fact]
    public void UseExternalizedPayloads_NamedClientBuilder_DoesNotLeakOptionsToOtherNames()
    {
        // Arrange - configure ONLY the "client-hub" name. This is the assertion with teeth: a happy-path named
        // test still passes even if every .Get(builder.Name) were hard-coded to Options.DefaultName, because the
        // requested name and the default would resolve to the same populated instance. Asserting that OTHER names
        // stay at their defaults is what actually pins the configuration to builder.Name.
        const string name = "client-hub";
        ServiceCollection services = new();
        Mock<IDurableTaskClientBuilder> builder = new();
        builder.Setup(b => b.Services).Returns(services);
        builder.Setup(b => b.Name).Returns(name);

        // Act
        builder.Object.UseExternalizedPayloads(
            options => options.ConnectionString = "UseDevelopmentStorage=true");
        using ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<LargePayloadStorageOptions> monitor =
            provider.GetRequiredService<IOptionsMonitor<LargePayloadStorageOptions>>();

        // Assert - neither a different name nor the default name observes "client-hub"'s configuration.
        monitor.Get("other-hub").ConnectionString.Should().NotBe("UseDevelopmentStorage=true");
        monitor.Get(string.Empty).ConnectionString.Should().NotBe("UseDevelopmentStorage=true");
    }

    [Fact]
    public async Task UseExternalizedPayloads_ClientAndWorkerInOneHost_ShareOneStore()
    {
        // Arrange - the combined "client triggers, worker executes" host: both AddDurableTaskClient and
        // AddDurableTaskWorker call UseExternalizedPayloads in one ServiceCollection. PayloadStore is registered
        // with TryAdd on both sides, so the container must hold exactly one.
        ServiceCollection services = new();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddDurableTaskClient(builder =>
        {
            builder.UseGrpc(options => options.CallInvoker = Mock.Of<CallInvoker>());
            builder.UseExternalizedPayloads(options => options.ConnectionString = "UseDevelopmentStorage=true");
        });
        services.AddDurableTaskWorker(builder =>
        {
            builder.UseGrpc(options => options.CallInvoker = Mock.Of<CallInvoker>());
            builder.UseExternalizedPayloads(options => options.ConnectionString = "UseDevelopmentStorage=true");
        });

        // AddDurableTaskClient registers a ClientContainer that is IAsyncDisposable-only, so the provider must be
        // disposed asynchronously.
        await using ServiceProvider provider = services.BuildServiceProvider();

        // Assert - one shared store, and the only hosted service is the worker itself: neither builder adds
        // background work of its own now that auto-purge is an explicit call. The worker's purge activity still
        // constructs in the combined container.
        provider.GetServices<PayloadStore>().Should().ContainSingle()
            .Which.Should().BeOfType<BlobPayloadStore>();
        provider.GetServices<IHostedService>().Should().ContainSingle()
            .Which.Should().BeOfType<GrpcDurableTaskWorker>();

        Action constructActivity = () =>
            ActivatorUtilities.GetServiceOrCreateInstance(provider, typeof(GetLargePayloadTombstonesActivity));
        constructActivity.Should().NotThrow();
    }
}
