// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Xunit;

namespace Microsoft.DurableTask.Extensions.Azure.Tests;

public class DurableTaskSchedulerOptionsTests
{
    private const string ValidEndpoint = "myaccount.westus3.durabletask.io";
    private const string ValidTaskHub = "testhub";

    [Fact]
    public void FromConnectionString_WithDefaultAzure_ShouldCreateValidInstance()
    {
        // Arrange
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=DefaultAzure;TaskHub={ValidTaskHub}";

        // Act
        var options = DurableTaskSchedulerOptions.FromConnectionString(connectionString);

        // Assert
        options.EndpointAddress.Should().Be($"https://{ValidEndpoint}");
        options.TaskHubName.Should().Be(ValidTaskHub);
        options.Credential.Should().BeOfType<DefaultAzureCredential>();
    }

    [Fact]
    public void FromConnectionString_WithManagedIdentity_ShouldCreateValidInstance()
    {
        // Arrange
        const string clientId = "00000000-0000-0000-0000-000000000000";
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=ManagedIdentity;ClientID={clientId};TaskHub={ValidTaskHub}";

        // Act
        var options = DurableTaskSchedulerOptions.FromConnectionString(connectionString);

        // Assert
        options.EndpointAddress.Should().Be($"https://{ValidEndpoint}");
        options.TaskHubName.Should().Be(ValidTaskHub);
        options.Credential.Should().BeOfType<ManagedIdentityCredential>();
    }

    [Fact]
    public void FromConnectionString_WithWorkloadIdentity_ShouldCreateValidInstance()
    {
        // Arrange
        const string clientId = "00000000-0000-0000-0000-000000000000";
        const string tenantId = "11111111-1111-1111-1111-111111111111";
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=WorkloadIdentity;ClientID={clientId};TenantId={tenantId};TaskHub={ValidTaskHub}";

        // Act
        var options = DurableTaskSchedulerOptions.FromConnectionString(connectionString);

        // Assert
        options.EndpointAddress.Should().Be($"https://{ValidEndpoint}");
        options.TaskHubName.Should().Be(ValidTaskHub);
        options.Credential.Should().BeOfType<WorkloadIdentityCredential>();
    }

    [Theory]
    [InlineData("Environment")]
    [InlineData("AzureCLI")]
    [InlineData("AzurePowerShell")]
    public void FromConnectionString_WithValidAuthTypes_ShouldCreateValidInstance(string authType)
    {
        // Arrange
        string connectionString = $"Endpoint={ValidEndpoint};Authentication={authType};TaskHub={ValidTaskHub}";

        // Act
        var options = DurableTaskSchedulerOptions.FromConnectionString(connectionString);

        // Assert
        options.EndpointAddress.Should().Be($"https://{ValidEndpoint}");
        options.TaskHubName.Should().Be(ValidTaskHub);
        options.Credential.Should().NotBeNull();
    }

    [Fact]
    public void FromConnectionString_WithInvalidAuthType_ShouldThrowArgumentException()
    {
        // Arrange
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=InvalidAuth;TaskHub={ValidTaskHub}";

        // Act & Assert
        var action = () => DurableTaskSchedulerOptions.FromConnectionString(connectionString);
        action.Should().Throw<ArgumentException>()
            .WithMessage("*contains an unsupported authentication type*");
    }

    [Fact]
    public void FromConnectionString_WithMissingRequiredProperties_ShouldThrowArgumentNullException()
    {
        // Arrange
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=DefaultAzure";  // Missing TaskHub

        // Act & Assert
        var action = () => DurableTaskSchedulerOptions.FromConnectionString(connectionString);
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FromConnectionString_WithNone_ShouldCreateInstanceWithNullCredential()
    {
        // Arrange
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=None;TaskHub={ValidTaskHub}";

        // Act
        var options = DurableTaskSchedulerOptions.FromConnectionString(connectionString);

        // Assert
        options.EndpointAddress.Should().Be($"https://{ValidEndpoint}");
        options.TaskHubName.Should().Be(ValidTaskHub);
        options.Credential.Should().BeNull();
    }
}
