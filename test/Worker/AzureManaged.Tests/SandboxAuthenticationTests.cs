// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.DurableTask.AzureManaged.Tests;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.DurableTask.Worker.AzureManaged.Sandboxes;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.DurableTask.Worker.AzureManaged.Tests;

[Collection("Scheduler authentication environment")]
public class SandboxAuthenticationTests(SchedulerAuthenticationServer server) : IClassFixture<SchedulerAuthenticationServer>
{
    [Theory]
    [InlineData(null, "https://durabletask.azure.us/.default", false)]
    [InlineData("https://durabletask.io", "https://durabletask.io/.default", false)]
    [InlineData("api://Custom/.default/.DEFAULT/", "api://Custom/.default/.default", false)]
    [InlineData("https://durabletask.azure.us", null, true)]
    public async Task Registration_ReconnectsWithSchedulerAudienceAndSharesTokenCacheAsync(
        string? resourceId, string? expectedScope, bool useCallerSuppliedChannel)
    {
        // Arrange
        using SchedulerEnvironmentVariable region = new("REGION_NAME", "UsDodCentral");
        using SchedulerEnvironmentVariable endpoint = new("DTS_ENDPOINT", server.Endpoint);
        using SchedulerEnvironmentVariable taskHub = new("DTS_TASK_HUB", "testhub");
        using SchedulerEnvironmentVariable workerProfile = new("DTS_WORKER_PROFILE_ID", "profile");
        using SchedulerEnvironmentVariable authentication = new("DTS_AUTHENTICATION", "ManagedIdentity");
        using SchedulerEnvironmentVariable identity = new("DTS_UMI_CLIENT_ID", "11111111-1111-1111-1111-111111111111");
        using SchedulerEnvironmentVariable sandboxProvider = new("DTS_SANDBOX_PROVIDER", "Sandbox");
        using SchedulerEnvironmentVariable sandboxId = new("DTS_SANDBOX_ID", "sandbox");
        RecordingSchedulerCredential credential = new(expireFirstToken: true);
        ServiceCollection services = new();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.Configure<DurableTaskRegistry>(registry =>
            registry.AddActivityFunc<string, string>("Hello", (_, input) => input));
        Mock<IDurableTaskWorkerBuilder> builder = new();
        builder.SetupGet(b => b.Services).Returns(services);
        builder.SetupGet(b => b.Name).Returns(Options.DefaultName);
        builder.Object.UseSandboxWorker();
        services.Configure<DurableTaskSchedulerWorkerOptions>(options => options.ResourceId = resourceId);
        using GrpcChannel suppliedChannel = GrpcChannel.ForAddress(server.Endpoint);
        if (useCallerSuppliedChannel)
        {
            services.PostConfigure<GrpcDurableTaskWorkerOptions>(
                options => options.CallInvoker = suppliedChannel.CreateCallInvoker());
        }

        await using ServiceProvider provider = services.BuildServiceProvider();
        DurableTaskSchedulerWorkerOptions schedulerOptions =
            provider.GetRequiredService<IOptionsMonitor<DurableTaskSchedulerWorkerOptions>>().CurrentValue;
        Assert.IsType<Azure.Identity.ManagedIdentityCredential>(schedulerOptions.Credential);
        schedulerOptions.Credential = credential;
        schedulerOptions.AllowInsecureCredentials = true;
        SandboxActivityWorkerRegistrationHostedService registration = Assert.Single(
            provider.GetServices<IHostedService>().OfType<SandboxActivityWorkerRegistrationHostedService>());
        GrpcChannel channel = Assert.IsType<GrpcChannel>(
            provider.GetRequiredService<IOptionsMonitor<GrpcDurableTaskWorkerOptions>>().CurrentValue.Channel);
        TaskCompletionSource firstRegistration = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource disconnect = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource secondRegistration = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int registrations = 0;
        server.RegistrationHandler = async (requests, context) =>
        {
            Assert.True(await requests.MoveNext(context.CancellationToken));
            Assert.Equal("testhub", Assert.Single(context.RequestHeaders, header => header.Key == "taskhub").Value);
            Assert.Equal(useCallerSuppliedChannel ? null : "Bearer recorded-token", context.RequestHeaders.GetValue("authorization"));
            if (Interlocked.Increment(ref registrations) == 1)
            {
                firstRegistration.TrySetResult();
                await disconnect.Task.WaitAsync(context.CancellationToken);
                throw new RpcException(new Status(StatusCode.Unavailable, "Test reconnect"));
            }

            secondRegistration.TrySetResult();
            while (await requests.MoveNext(context.CancellationToken))
            {
            }

            return [];
        };
        Assert.Empty(credential.Scopes);

        try
        {
            // Act
            await registration.StartAsync(CancellationToken.None);
            await firstRegistration.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Environment.SetEnvironmentVariable("REGION_NAME", "westus2");
            disconnect.SetResult();
            await secondRegistration.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (!useCallerSuppliedChannel)
            {
                await server.CallAsync(channel);
            }

            // Assert
            Assert.Equal(useCallerSuppliedChannel ? [] : new[] { expectedScope, expectedScope }, credential.Scopes);
            Assert.Equal(2, registrations);
        }
        finally
        {
            using CancellationTokenSource stop = new(TimeSpan.FromSeconds(10));
            await registration.StopAsync(stop.Token);
            server.RegistrationHandler = null;
        }
    }
}
