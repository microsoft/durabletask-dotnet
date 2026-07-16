// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
    public void UseExternalizedPayloads_WithAutoPurgeDisabled_DoesNotRegisterHostedPurgeStarter()
    {
        // Arrange
        ServiceCollection services = new();
        Mock<IDurableTaskClientBuilder> builder = new();
        builder.Setup(b => b.Services).Returns(services);
        builder.Setup(b => b.Name).Returns(string.Empty);

        // Act - auto-purge left at its default (false).
        builder.Object.UseExternalizedPayloads(options => { });

        // Assert
        services.Should().NotContain(d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void UseExternalizedPayloads_NoArgOverload_DoesNotRegisterHostedPurgeStarter()
    {
        // Arrange
        ServiceCollection services = new();
        Mock<IDurableTaskClientBuilder> builder = new();
        builder.Setup(b => b.Services).Returns(services);
        builder.Setup(b => b.Name).Returns(string.Empty);

        // Act - the shared-store overload never enables auto-purge.
        builder.Object.UseExternalizedPayloads();

        // Assert
        services.Should().NotContain(d => d.ServiceType == typeof(IHostedService));
    }
}
