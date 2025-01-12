// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using System.Data.Common;
using Xunit;

namespace Microsoft.DurableTask.Shared.AzureManaged.Tests;

public class DurableTaskSchedulerConnectionStringTests
{
    private const string ValidEndpoint = "myaccount.westus3.durabletask.io";
    private const string ValidTaskHub = "testhub";
    private const string ValidClientId = "00000000-0000-0000-0000-000000000000";
    private const string ValidTenantId = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public void Constructor_WithValidConnectionString_ShouldParseCorrectly()
    {
        // Arrange
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=DefaultAzure;TaskHub={ValidTaskHub}";

        // Act
        var parsedConnectionString = new DurableTaskSchedulerConnectionString(connectionString);

        // Assert
        parsedConnectionString.Endpoint.Should().Be(ValidEndpoint);
        parsedConnectionString.TaskHubName.Should().Be(ValidTaskHub);
        parsedConnectionString.Authentication.Should().Be("DefaultAzure");
    }

    [Fact]
    public void Constructor_WithManagedIdentity_ShouldParseClientId()
    {
        // Arrange
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=ManagedIdentity;ClientID={ValidClientId};TaskHub={ValidTaskHub}";

        // Act
        var parsedConnectionString = new DurableTaskSchedulerConnectionString(connectionString);

        // Assert
        parsedConnectionString.ClientId.Should().Be(ValidClientId);
    }

    [Fact]
    public void Constructor_WithWorkloadIdentity_ShouldParseAllProperties()
    {
        // Arrange
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=WorkloadIdentity;ClientID={ValidClientId};TenantId={ValidTenantId};TaskHub={ValidTaskHub}";

        // Act
        var parsedConnectionString = new DurableTaskSchedulerConnectionString(connectionString);

        // Assert
        parsedConnectionString.ClientId.Should().Be(ValidClientId);
        parsedConnectionString.TenantId.Should().Be(ValidTenantId);
    }

    [Fact]
    public void Constructor_WithAdditionallyAllowedTenants_ShouldParseTenantList()
    {
        // Arrange
        const string tenants = "tenant1,tenant2,tenant3";
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=WorkloadIdentity;AdditionallyAllowedTenants={tenants};TaskHub={ValidTaskHub}";

        // Act
        var parsedConnectionString = new DurableTaskSchedulerConnectionString(connectionString);

        // Assert
        parsedConnectionString.AdditionallyAllowedTenants.Should().NotBeNull();
        parsedConnectionString.AdditionallyAllowedTenants.Should().BeEquivalentTo(new[] { "tenant1", "tenant2", "tenant3" });
    }

    [Fact]
    public void Constructor_WithMultipleAdditionallyAllowedTenants_ShouldParseCorrectly()
    {
        // Arrange
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=WorkloadIdentity;TaskHub={ValidTaskHub};" +
            "AdditionallyAllowedTenants=tenant1,tenant2,tenant3";

        // Act
        var parsedConnectionString = new DurableTaskSchedulerConnectionString(connectionString);

        // Assert
        parsedConnectionString.AdditionallyAllowedTenants.Should().NotBeNull();
        parsedConnectionString.AdditionallyAllowedTenants!.Should().HaveCount(3);
        parsedConnectionString.AdditionallyAllowedTenants.Should().Contain(new[] { "tenant1", "tenant2", "tenant3" });
    }

    [Fact]
    public void Constructor_WithCaseInsensitivePropertyNames_ShouldParseCorrectly()
    {
        // Arrange
        string connectionString = $"endpoint={ValidEndpoint};AUTHENTICATION=DefaultAzure;taskhub={ValidTaskHub};" +
            $"clientid={ValidClientId};tenantid={ValidTenantId}";

        // Act
        var parsedConnectionString = new DurableTaskSchedulerConnectionString(connectionString);

        // Assert
        parsedConnectionString.Endpoint.Should().Be(ValidEndpoint);
        parsedConnectionString.Authentication.Should().Be("DefaultAzure");
        parsedConnectionString.TaskHubName.Should().Be(ValidTaskHub);
        parsedConnectionString.ClientId.Should().Be(ValidClientId);
        parsedConnectionString.TenantId.Should().Be(ValidTenantId);
    }

    [Fact]
    public void Constructor_WithInvalidConnectionStringFormat_ShouldThrowFormatException()
    {
        // Arrange
        var connectionString = "This is not a valid=connection string format";

        // Act & Assert
        var action = () => _ = new DurableTaskSchedulerConnectionString(connectionString).Endpoint;
        action.Should().Throw<ArgumentNullException>()
            .WithMessage("Value cannot be null. (Parameter 'The connection string is missing the required 'Endpoint' property.')");
    }

    [Fact]
    public void Constructor_WithEmptyConnectionString_ShouldThrowArgumentNullException()
    {
        // Arrange
        var connectionString = string.Empty;

        // Act & Assert
        var action = () => _ = new DurableTaskSchedulerConnectionString(connectionString).Endpoint;
        action.Should().Throw<ArgumentNullException>()
            .WithMessage("Value cannot be null. (Parameter 'The connection string is missing the required 'Endpoint' property.')");
    }

    [Fact]
    public void Constructor_WithDuplicateKeys_ShouldUseLastValue()
    {
        // Arrange
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=DefaultAzure;TaskHub=hub1;TaskHub=hub2";

        // Act
        var parsedConnectionString = new DurableTaskSchedulerConnectionString(connectionString);

        // Assert
        parsedConnectionString.TaskHubName.Should().Be("hub2");
    }

    [Fact]
    public void Constructor_WithMissingRequiredProperties_ShouldThrowArgumentNullException()
    {
        // Arrange
        string connectionString = $"Authentication=DefaultAzure;TaskHub={ValidTaskHub}"; // Missing Endpoint

        // Act & Assert
        var action = () => _ = new DurableTaskSchedulerConnectionString(connectionString).Endpoint;
        action.Should().Throw<ArgumentNullException>()
            .WithMessage("*'Endpoint' property*");
    }

    [Fact]
    public void Constructor_WithInvalidConnectionString_ShouldThrowArgumentException()
    {
        // Arrange
        string connectionString = "This is not a valid connection string";

        // Act & Assert
        var action = () => new DurableTaskSchedulerConnectionString(connectionString);
        action.Should().Throw<ArgumentException>()
            .WithMessage("*Format of the initialization string does not conform to specification*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Constructor_WithNullOrEmptyConnectionString_ShouldThrowArgumentNullException(string? connectionString)
    {
        // Act & Assert
        var action = () => _ = new DurableTaskSchedulerConnectionString(connectionString!).Endpoint;
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetValue_WithNonExistentProperty_ShouldReturnNull()
    {
        // Arrange
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=DefaultAzure;TaskHub={ValidTaskHub}";
        var parsedConnectionString = new DurableTaskSchedulerConnectionString(connectionString);

        // Assert
        parsedConnectionString.ClientId.Should().BeNull();
        parsedConnectionString.TenantId.Should().BeNull();
        parsedConnectionString.TokenFilePath.Should().BeNull();
        parsedConnectionString.AdditionallyAllowedTenants.Should().BeNull();
    }
}
