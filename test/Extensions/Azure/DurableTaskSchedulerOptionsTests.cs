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
        options.EndpointAddress.Should().Be(ValidEndpoint);
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
        options.EndpointAddress.Should().Be(ValidEndpoint);
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
        options.EndpointAddress.Should().Be(ValidEndpoint);
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
        options.EndpointAddress.Should().Be(ValidEndpoint);
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
        options.EndpointAddress.Should().Be(ValidEndpoint);
        options.TaskHubName.Should().Be(ValidTaskHub);
        options.Credential.Should().BeNull();
    }

    [Fact]
    public void DefaultProperties_ShouldHaveExpectedValues()
    {
        // Arrange & Act
        var options = new DurableTaskSchedulerOptions();

        // Assert
        options.ResourceId.Should().Be("https://durabletask.io");
        options.WorkerId.Should().NotBeNullOrEmpty();
        options.WorkerId.Should().Contain(Environment.MachineName);
        options.WorkerId.Should().Contain(Environment.ProcessId.ToString());
        options.AllowInsecureCredentials.Should().BeFalse();
    }

    [Fact]
    public void CreateChannel_WithHttpsEndpoint_ShouldCreateSecureChannel()
    {
        // Arrange
        var options = new DurableTaskSchedulerOptions
        {
            EndpointAddress = $"https://{ValidEndpoint}",
            TaskHubName = ValidTaskHub,
            Credential = new DefaultAzureCredential()
        };

        // Act
        var channel = options.CreateChannel();

        // Assert
        channel.Should().NotBeNull();
    }

    [Fact]
    public void CreateChannel_WithHttpEndpoint_ShouldCreateInsecureChannel()
    {
        // Arrange
        var options = new DurableTaskSchedulerOptions
        {
            EndpointAddress = $"http://{ValidEndpoint}",
            TaskHubName = ValidTaskHub,
            AllowInsecureCredentials = true
        };

        // Act
        var channel = options.CreateChannel();

        // Assert
        channel.Should().NotBeNull();
    }

    [Fact]
    public void FromConnectionString_WithInvalidEndpoint_ShouldThrowArgumentException()
    {
        // Arrange
        var connectionString = "Endpoint=not a valid endpoint;Authentication=DefaultAzure;TaskHub=testhub;";

        // Act & Assert
        var options = DurableTaskSchedulerOptions.FromConnectionString(connectionString);
        var action = () => options.CreateChannel();
        action.Should().Throw<UriFormatException>()
            .WithMessage("Invalid URI: The hostname could not be parsed.");
    }

    [Fact]
    public void FromConnectionString_WithoutProtocol_ShouldPreserveEndpoint()
    {
        // Arrange
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=DefaultAzure;TaskHub={ValidTaskHub}";

        // Act
        var options = DurableTaskSchedulerOptions.FromConnectionString(connectionString);

        // Assert
        options.EndpointAddress.Should().Be(ValidEndpoint);
    }

    [Fact]
    public void CreateChannel_ShouldAddHttpsPrefix()
    {
        // Arrange
        var options = new DurableTaskSchedulerOptions
        {
            EndpointAddress = ValidEndpoint,
            TaskHubName = ValidTaskHub,
            Credential = new DefaultAzureCredential()
        };

        // Act
        var channel = options.CreateChannel();

        // Assert
        channel.Should().NotBeNull();
        // Note: We can't directly test the endpoint in the channel as it's not exposed
    }
}
