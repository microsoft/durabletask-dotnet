// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Identity;
using FluentAssertions;
using Xunit;

namespace Microsoft.DurableTask.Shared.AzureManaged.Tests;

public class DurableTaskSchedulerClientOptionsTests
{
    const string ValidEndpoint = "myaccount.westus3.durabletask.io";
    const string ValidTaskHub = "testhub";

    [Fact]
    public void FromConnectionString_WithDefaultAzure_ShouldCreateValidInstance()
    {
        // Arrange
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=DefaultAzure;TaskHub={ValidTaskHub}";

        // Act
        DurableTaskSchedulerClientOptions options = DurableTaskSchedulerClientOptions.FromConnectionString(connectionString);

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
        DurableTaskSchedulerClientOptions options = DurableTaskSchedulerClientOptions.FromConnectionString(connectionString);

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
        DurableTaskSchedulerClientOptions options = DurableTaskSchedulerClientOptions.FromConnectionString(connectionString);

        // Assert
        options.EndpointAddress.Should().Be(ValidEndpoint);
        options.TaskHubName.Should().Be(ValidTaskHub);
        options.Credential.Should().BeOfType<WorkloadIdentityCredential>();
    }


    [Fact]
    public void FromConnectionString_WithEnvironmentCredential_ShouldCreateValidInstance()
    {
        // Arrange
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=Environment;TaskHub={ValidTaskHub}";

        // Act
        DurableTaskSchedulerClientOptions options = DurableTaskSchedulerClientOptions.FromConnectionString(connectionString);

        // Assert
        options.EndpointAddress.Should().Be(ValidEndpoint);
        options.TaskHubName.Should().Be(ValidTaskHub);
        options.Credential.Should().BeOfType<EnvironmentCredential>();
    }

    [Fact]
    public void FromConnectionString_WithAzureCliCredential_ShouldCreateValidInstance()
    {
        // Arrange
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=AzureCLI;TaskHub={ValidTaskHub}";

        // Act
        DurableTaskSchedulerClientOptions options = DurableTaskSchedulerClientOptions.FromConnectionString(connectionString);

        // Assert
        options.EndpointAddress.Should().Be(ValidEndpoint);
        options.TaskHubName.Should().Be(ValidTaskHub);
        options.Credential.Should().BeOfType<AzureCliCredential>();
    }

    [Fact]
    public void FromConnectionString_WithAzurePowerShellCredential_ShouldCreateValidInstance()
    {
        // Arrange
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=AzurePowerShell;TaskHub={ValidTaskHub}";

        // Act
        DurableTaskSchedulerClientOptions options = DurableTaskSchedulerClientOptions.FromConnectionString(connectionString);

        // Assert
        options.EndpointAddress.Should().Be(ValidEndpoint);
        options.TaskHubName.Should().Be(ValidTaskHub);
        options.Credential.Should().BeOfType<AzurePowerShellCredential>();
    }

    [Fact]
    public void FromConnectionString_WithVisualStudioCredential_ShouldCreateValidInstance()
    {
        // Arrange
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=VisualStudio;TaskHub={ValidTaskHub}";

        // Act
        DurableTaskSchedulerClientOptions options = DurableTaskSchedulerClientOptions.FromConnectionString(connectionString);

        // Assert
        options.EndpointAddress.Should().Be(ValidEndpoint);
        options.TaskHubName.Should().Be(ValidTaskHub);
        options.Credential.Should().BeOfType<VisualStudioCredential>();
    }

    [Fact]
    public void FromConnectionString_WithVisualStudioCodeCredential_ShouldCreateValidInstance()
    {
        // Arrange
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=VisualStudioCode;TaskHub={ValidTaskHub}";

        // Act
        DurableTaskSchedulerClientOptions options = DurableTaskSchedulerClientOptions.FromConnectionString(connectionString);

        // Assert
        options.EndpointAddress.Should().Be(ValidEndpoint);
        options.TaskHubName.Should().Be(ValidTaskHub);
        options.Credential.Should().BeOfType<VisualStudioCodeCredential>();
    }

    [Fact]
    public void FromConnectionString_WithInteractiveCredential_ShouldCreateValidInstance()
    {
        // Arrange
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=InteractiveBrowser;TaskHub={ValidTaskHub}";

        // Act
        DurableTaskSchedulerClientOptions options = DurableTaskSchedulerClientOptions.FromConnectionString(connectionString);

        // Assert
        options.EndpointAddress.Should().Be(ValidEndpoint);
        options.TaskHubName.Should().Be(ValidTaskHub);
        options.Credential.Should().BeOfType<InteractiveBrowserCredential>();
    }

    [Fact]
    public void FromConnectionString_WithInvalidAuthType_ShouldThrowArgumentException()
    {
        // Arrange
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=InvalidAuth;TaskHub={ValidTaskHub}";

        // Act & Assert
        Action action = () => DurableTaskSchedulerClientOptions.FromConnectionString(connectionString);
        action.Should().Throw<ArgumentException>()
            .WithMessage("*contains an unsupported authentication type*");
    }

    [Fact]
    public void FromConnectionString_WithMissingRequiredProperties_ShouldThrowArgumentNullException()
    {
        // Arrange
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=DefaultAzure";  // Missing TaskHub

        // Act & Assert
        Action action = () => DurableTaskSchedulerClientOptions.FromConnectionString(connectionString);
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FromConnectionString_WithNone_ShouldCreateInstanceWithNullCredential()
    {
        // Arrange
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=None;TaskHub={ValidTaskHub}";

        // Act
        DurableTaskSchedulerClientOptions options = DurableTaskSchedulerClientOptions.FromConnectionString(connectionString);

        // Assert
        options.EndpointAddress.Should().Be(ValidEndpoint);
        options.TaskHubName.Should().Be(ValidTaskHub);
        options.Credential.Should().BeNull();
    }

    [Fact]
    public void DefaultProperties_ShouldHaveExpectedValues()
    {
        // Arrange & Act
        DurableTaskSchedulerClientOptions options = new DurableTaskSchedulerClientOptions();

        // Assert
        options.ResourceId.Should().Be("https://durabletask.io");
        options.AllowInsecureCredentials.Should().BeFalse();
    }

    [Fact]
    public void CreateChannel_WithHttpsEndpoint_ShouldCreateSecureChannel()
    {
        // Arrange
        DurableTaskSchedulerClientOptions options = new DurableTaskSchedulerClientOptions
        {
            EndpointAddress = $"https://{ValidEndpoint}",
            TaskHubName = ValidTaskHub,
            Credential = new DefaultAzureCredential()
        };

        // Act
        using Grpc.Net.Client.GrpcChannel channel = options.CreateChannel();

        // Assert
        channel.Should().NotBeNull();
    }

    [Fact]
    public void CreateChannel_WithHttpEndpoint_ShouldCreateInsecureChannel()
    {
        // Arrange
        DurableTaskSchedulerClientOptions options = new DurableTaskSchedulerClientOptions
        {
            EndpointAddress = $"http://{ValidEndpoint}",
            TaskHubName = ValidTaskHub,
            AllowInsecureCredentials = true
        };

        // Act
        using Grpc.Net.Client.GrpcChannel channel = options.CreateChannel();

        // Assert
        channel.Should().NotBeNull();
    }

    [Fact]
    public void FromConnectionString_WithInvalidEndpoint_ShouldThrowArgumentException()
    {
        // Arrange
        string connectionString = "Endpoint=not a valid endpoint;Authentication=DefaultAzure;TaskHub=testhub;";

        // Act & Assert
        DurableTaskSchedulerClientOptions options = DurableTaskSchedulerClientOptions.FromConnectionString(connectionString);
        Action action = () => options.CreateChannel();
        action.Should().Throw<UriFormatException>()
            .WithMessage("Invalid URI: The hostname could not be parsed.");
    }

    [Fact]
    public void FromConnectionString_WithoutProtocol_ShouldPreserveEndpoint()
    {
        // Arrange
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=DefaultAzure;TaskHub={ValidTaskHub}";

        // Act
        DurableTaskSchedulerClientOptions options = DurableTaskSchedulerClientOptions.FromConnectionString(connectionString);

        // Assert
        options.EndpointAddress.Should().Be(ValidEndpoint);
    }

    [Fact]
    public void CreateChannel_ShouldAddHttpsPrefix()
    {
        // Arrange
        DurableTaskSchedulerClientOptions options = new DurableTaskSchedulerClientOptions
        {
            EndpointAddress = ValidEndpoint,
            TaskHubName = ValidTaskHub,
            Credential = new DefaultAzureCredential()
        };

        // Act
        using Grpc.Net.Client.GrpcChannel channel = options.CreateChannel();

        // Assert
        channel.Should().NotBeNull();
        // Note: We can't directly test the endpoint in the channel as it's not exposed
    }
}
