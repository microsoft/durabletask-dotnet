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
    public void UseExternalizedPayloads_WithAutoPurgeEnabled_RegistersHostedPurgeStarter()
    {
        // Arrange
        ServiceCollection services = new();
        Mock<IDurableTaskClientBuilder> builder = new();
        builder.Setup(b => b.Services).Returns(services);
        builder.Setup(b => b.Name).Returns(string.Empty);

        // Act
        builder.Object.UseExternalizedPayloads(options => options.AutoPurge = true);

        // Assert - the purge-job starter is the only IHostedService this path registers.
        services.Should().ContainSingle(d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void UseExternalizedPayloads_WithAutoPurgeDisabled_StillRegistersHostedPurgeStarter()
    {
        // Arrange
        ServiceCollection services = new();
        Mock<IDurableTaskClientBuilder> builder = new();
        builder.Setup(b => b.Services).Returns(services);
        builder.Setup(b => b.Name).Returns(string.Empty);

        // Act - auto-purge left at its default (false).
        builder.Object.UseExternalizedPayloads(options => { });

        // Assert - the starter is registered unconditionally now; whether auto-purge is enabled can only be
        // known once options are fully resolved, so the no-op moved into BlobPurgeJobStarter.StartAsync. Do not
        // "fix" this back to NotContain - the registration is intentional.
        services.Should().ContainSingle(d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void UseExternalizedPayloads_AutoPurgeViaServicesConfigure_RegistersResolvableStarter()
    {
        // Arrange - the enable path that silently no-op'd before: AutoPurge set through services.Configure (not
        // the inline delegate) plus the parameterless overload. The old probe-at-registration only saw the
        // inline delegate, so the starter was never registered. It must now be registered and, more importantly,
        // resolvable from the built provider.
        ServiceCollection services = new();
        services.AddSingleton<ILogger<BlobPurgeJobStarter>>(NullLogger<BlobPurgeJobStarter>.Instance);
        services.AddSingleton(Mock.Of<IDurableTaskClientProvider>());
        services.Configure<LargePayloadStorageOptions>(o =>
        {
            o.AutoPurge = true;
            o.ConnectionString = "UseDevelopmentStorage=true";
        });

        Mock<IDurableTaskClientBuilder> builder = new();
        builder.Setup(b => b.Services).Returns(services);
        builder.Setup(b => b.Name).Returns(string.Empty);

        // Act - the parameterless overload; AutoPurge comes from services.Configure above.
        builder.Object.UseExternalizedPayloads();
        using ServiceProvider provider = services.BuildServiceProvider();

        // Assert - the starter resolves as a hosted service (construction pulls PayloadStore, the client
        // provider, options and logger), proving the whole enable path is wired end to end.
        provider.GetServices<IHostedService>().OfType<BlobPurgeJobStarter>().Should().ContainSingle();
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UseExternalizedPayloads_Worker_SendsResolvedAutoPurgeValueOnTheHandshake(bool autoPurge)
    {
        // Arrange - the backend only tombstones payloads for a task hub whose workers opted in, so the resolved
        // AutoPurge value has to ride the GetWorkItems handshake. A worker that never calls this extension sends
        // nothing at all, which the backend reads as "no opinion".
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> builder = new();
        builder.Setup(b => b.Services).Returns(services);
        builder.Setup(b => b.Name).Returns(string.Empty);
        services.AddOptions<GrpcDurableTaskWorkerOptions>(string.Empty)
            .Configure(o => o.CallInvoker = Mock.Of<CallInvoker>());

        // Act
        builder.Object.UseExternalizedPayloads(options =>
        {
            options.ConnectionString = "UseDevelopmentStorage=true";
            options.AutoPurge = autoPurge;
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        GrpcDurableTaskWorkerOptions grpcOptions =
            provider.GetRequiredService<IOptionsMonitor<GrpcDurableTaskWorkerOptions>>().Get(string.Empty);

        // Assert - an explicit true/false, never left absent, because calling this extension IS the choice.
        grpcOptions.LargePayloadAutoPurgeEnabled.Should().Be(autoPurge);
    }

    [Fact]
    public void UseExternalizedPayloads_Client_EnablesEntitySupport()
    {
        // Arrange - the auto-purge starter reaches the singleton job through client.Entities on BOTH the enabled
        // and disabled paths, so the client must turn entity support on whenever externalized payloads are
        // configured. It must not be gated on AutoPurge, so leave AutoPurge at its default (false) here.
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
        // payloads are configured. Not gated on AutoPurge, so leave AutoPurge at its default (false) here.
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
        // inject the worker's own TaskHubSidecarServiceClient (built from the worker's GrpcDurableTaskWorkerOptions),
        // so they construct with no DurableTaskClient present. A real worker configures a transport, so UseGrpc
        // supplies one here - without it the worker's PostConfigure would throw for having neither Channel nor
        // CallInvoker.
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
        // path had no coverage: the storage options, the client's entity-support flip, the intercepted gRPC
        // client options and the purge starter are ALL keyed on builder.Name. Drive a non-empty name end to end.
        const string name = "client-hub";
        ServiceCollection services = new();
        services.AddSingleton<ILogger<BlobPurgeJobStarter>>(NullLogger<BlobPurgeJobStarter>.Instance);
        services.AddSingleton(Mock.Of<IDurableTaskClientProvider>());
        services.AddOptions<GrpcDurableTaskClientOptions>(name)
            .Configure(o => o.CallInvoker = Mock.Of<CallInvoker>());

        Mock<IDurableTaskClientBuilder> builder = new();
        builder.Setup(b => b.Services).Returns(services);
        builder.Setup(b => b.Name).Returns(name);

        // Act
        builder.Object.UseExternalizedPayloads(options =>
        {
            options.ConnectionString = "UseDevelopmentStorage=true";
            options.AutoPurge = true;
        });
        using ServiceProvider provider = services.BuildServiceProvider();

        // Assert - the storage options, the entity-support flip and the intercepted gRPC client options all
        // materialize under the builder name, and the starter and store resolve from the built provider.
        provider.GetRequiredService<IOptionsMonitor<LargePayloadStorageOptions>>().Get(name)
            .AutoPurge.Should().BeTrue();
        provider.GetRequiredService<IOptionsMonitor<DurableTaskClientOptions>>().Get(name)
            .EnableEntitySupport.Should().BeTrue();
        provider.GetRequiredService<IOptionsMonitor<GrpcDurableTaskClientOptions>>().Get(name)
            .CallInvoker.Should().NotBeNull();
        provider.GetServices<IHostedService>().OfType<BlobPurgeJobStarter>().Should().ContainSingle();
        provider.GetRequiredService<PayloadStore>().Should().BeOfType<BlobPayloadStore>();
    }

    [Fact]
    public void UseExternalizedPayloads_NamedWorkerBuilder_ResolvesEverythingUnderThatName()
    {
        // Arrange - the worker mirror of the named-client gap: the storage options, the worker's entity-support
        // flip, the resolved auto-purge handshake flag and the sidecar client the activities inject are all keyed
        // on builder.Name, and every existing worker test uses the empty name. Drive a non-empty name.
        const string name = "worker-hub";
        ServiceCollection services = new();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddOptions<GrpcDurableTaskWorkerOptions>(name)
            .Configure(o => o.CallInvoker = Mock.Of<CallInvoker>());

        Mock<IDurableTaskWorkerBuilder> builder = new();
        builder.Setup(b => b.Services).Returns(services);
        builder.Setup(b => b.Name).Returns(name);

        // Act
        builder.Object.UseExternalizedPayloads(options =>
        {
            options.ConnectionString = "UseDevelopmentStorage=true";
            options.AutoPurge = true;
        });
        using ServiceProvider provider = services.BuildServiceProvider();

        // Assert - all three option sets resolve under the name, the store is blob-backed, and both purge
        // activities construct through the exact reflection path DurableTaskRegistry uses at dispatch.
        provider.GetRequiredService<IOptionsMonitor<LargePayloadStorageOptions>>().Get(name)
            .AutoPurge.Should().BeTrue();
        provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerOptions>>().Get(name)
            .EnableEntitySupport.Should().BeTrue();
        provider.GetRequiredService<IOptionsMonitor<GrpcDurableTaskWorkerOptions>>().Get(name)
            .LargePayloadAutoPurgeEnabled.Should().BeTrue();
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
        builder.Object.UseExternalizedPayloads(options =>
        {
            options.ConnectionString = "UseDevelopmentStorage=true";
            options.AutoPurge = true;
        });
        using ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<LargePayloadStorageOptions> monitor =
            provider.GetRequiredService<IOptionsMonitor<LargePayloadStorageOptions>>();

        // Assert - neither a different name nor the default name observes "client-hub"'s configuration.
        monitor.Get("other-hub").AutoPurge.Should().BeFalse();
        monitor.Get("other-hub").ConnectionString.Should().NotBe("UseDevelopmentStorage=true");
        monitor.Get(string.Empty).AutoPurge.Should().BeFalse();
        monitor.Get(string.Empty).ConnectionString.Should().NotBe("UseDevelopmentStorage=true");
    }

    [Fact]
    public async Task UseExternalizedPayloads_ClientAndWorkerInOneHost_ShareOneStoreAndOneStarter()
    {
        // Arrange - the combined "client triggers, worker executes" host: both AddDurableTaskClient and
        // AddDurableTaskWorker call UseExternalizedPayloads in one ServiceCollection. PayloadStore is registered
        // with TryAdd on both sides, so the container must hold exactly one; and only the client registers the
        // purge starter, so there must be exactly one of those too - not one per builder.
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

        // Assert - one shared store, one starter (client-registered), and the worker's purge activity still
        // constructs in the combined container.
        provider.GetServices<PayloadStore>().Should().ContainSingle()
            .Which.Should().BeOfType<BlobPayloadStore>();
        provider.GetServices<IHostedService>().OfType<BlobPurgeJobStarter>().Should().ContainSingle();

        Action constructActivity = () =>
            ActivatorUtilities.GetServiceOrCreateInstance(provider, typeof(GetLargePayloadTombstonesActivity));
        constructActivity.Should().NotThrow();
    }
}
