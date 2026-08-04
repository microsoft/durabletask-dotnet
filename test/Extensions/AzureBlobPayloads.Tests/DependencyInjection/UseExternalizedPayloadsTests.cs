// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Client;
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
        using ServiceProvider provider = services.BuildServiceProvider();

        // Assert - the store resolves without throwing and is the blob-backed implementation.
        PayloadStore store = provider.GetRequiredService<PayloadStore>();
        store.Should().BeOfType<BlobPayloadStore>();
    }
}
