// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.DurableTask.AzureManaged.Tests;
using Microsoft.DurableTask.Client.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.DurableTask.Client.AzureManaged.Tests;

[Collection("Scheduler authentication environment")]
public class SandboxAuthenticationTests(SchedulerAuthenticationServer server) : IClassFixture<SchedulerAuthenticationServer>
{
    [Theory]
    [InlineData(null, "https://durabletask.azure.us/.default")]
    [InlineData("api://Custom/.default/.DEFAULT/", "api://Custom/.default/.default")]
    public async Task Management_UsesSchedulerAudienceAndTokenCacheAsync(string? resourceId, string expectedScope)
    {
        // Arrange
        using SchedulerEnvironmentVariable region = new("REGION_NAME", "UsGovVirginia");
        RecordingSchedulerCredential credential = new(expireFirstToken: true);
        ServiceCollection services = new();
        Mock<IDurableTaskClientBuilder> builder = new();
        builder.SetupGet(b => b.Services).Returns(services);
        builder.SetupGet(b => b.Name).Returns("sandbox");
        builder.Object.UseDurableTaskScheduler(
            $"Endpoint={server.Endpoint};TaskHub=testhub;Authentication=None;ResourceId=\"{resourceId}\"",
            options => options.Credential = credential);
        services.AddDurableTaskSchedulerSandboxActivitiesClient("sandbox");
        await using ServiceProvider provider = services.BuildServiceProvider();
        SandboxActivitiesClient client = provider.GetRequiredService<SandboxActivitiesClient>();
        GrpcChannel channel = Assert.IsType<GrpcChannel>(
            provider.GetRequiredService<IOptionsMonitor<GrpcDurableTaskClientOptions>>().Get("sandbox").Channel);
        Assert.Empty(credential.Scopes);
        server.ManagementHeaders.Clear();

        // Act
        await server.CallAsync(channel);
        Environment.SetEnvironmentVariable("REGION_NAME", "westus2");
        await client.EnableSandboxActivitiesAsync();
        await client.RemoveSandboxWorkerProfileAsync("profile");
        await server.CallAsync(channel);

        // Assert
        Assert.Equal([expectedScope, expectedScope], credential.Scopes);
        Assert.True(server.ManagementHeaders.Count >= 2, "Both profile declaration and removal must use the authenticated transport.");
        Assert.All(server.ManagementHeaders, headers =>
        {
            Assert.Equal("testhub", Assert.Single(headers, header => header.Key == "taskhub").Value);
            Assert.Equal("Bearer recorded-token", headers.GetValue("authorization"));
        });
    }

    [Fact]
    public async Task Management_CallerSuppliedChannelRemainsResponsibleForAuthenticationAsync()
    {
        // Arrange
        RecordingSchedulerCredential unusedCredential = new();
        ServiceCollection services = new();
        services.Configure<DurableTaskSchedulerClientOptions>(options =>
        {
            options.TaskHubName = "testhub";
            options.ResourceId = "https://durabletask.azure.us";
            options.Credential = unusedCredential;
        });
        using GrpcChannel suppliedChannel = GrpcChannel.ForAddress(server.Endpoint);
        services.Configure<GrpcDurableTaskClientOptions>(options => options.CallInvoker = suppliedChannel.CreateCallInvoker());
        services.AddDurableTaskSchedulerSandboxActivitiesClient();
        await using ServiceProvider provider = services.BuildServiceProvider();
        server.ManagementHeaders.Clear();

        // Act
        await provider.GetRequiredService<SandboxActivitiesClient>().RemoveSandboxWorkerProfileAsync("profile");

        // Assert
        Assert.Empty(unusedCredential.Scopes);
        Metadata headers = Assert.Single(server.ManagementHeaders);
        Assert.Null(headers.GetValue("authorization"));
        Assert.Equal("testhub", Assert.Single(headers, header => header.Key == "taskhub").Value);
    }
}
