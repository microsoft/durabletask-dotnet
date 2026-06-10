// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Grpc.Core;
using Microsoft.DurableTask.Client.Grpc;
using Microsoft.DurableTask.Protobuf.OnDemandSandbox;
using Microsoft.DurableTask.Worker.AzureManaged.OnDemandSandbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.DurableTask.Client.AzureManaged.Tests;

public class OnDemandSandboxActivitiesClientExtensionsTests
{
    [Fact]
    public async Task AddDurableTaskSchedulerOnDemandSandboxActivitiesClient_UsesConfiguredDurableTaskClientInvoker()
    {
        // Arrange
        RecordingOnDemandSandboxLogCallInvoker callInvoker = new();
        ServiceCollection services = new();
        services.AddOptions<DurableTaskSchedulerClientOptions>(Options.DefaultName)
            .Configure(options => options.TaskHubName = "client-test-taskhub");
        services.AddOptions<GrpcDurableTaskClientOptions>(Options.DefaultName)
            .Configure(options => options.CallInvoker = callInvoker);
        services.AddDurableTaskSchedulerOnDemandSandboxActivitiesClient();

        using ServiceProvider provider = services.BuildServiceProvider();
        OnDemandSandboxActivitiesClient client = provider.GetRequiredService<OnDemandSandboxActivitiesClient>();

        // Act
        await client.RemoveOnDemandSandboxActivityDeclarationAsync("default");

        // Assert
        callInvoker.RemoveRequest.Should().NotBeNull();
        callInvoker.RemoveRequest!.WorkerProfileId.Should().Be("default");
    }

    [Fact]
    public async Task EnableOnDemandSandboxActivitiesAsync_SendsWorkerProfileDeclarations()
    {
        // Arrange
        RecordingOnDemandSandboxLogCallInvoker callInvoker = new();
        OnDemandSandboxActivitiesClient client = new(
            new OnDemandSandboxActivities.OnDemandSandboxActivitiesClient(callInvoker),
            "client-test-taskhub");

        // Act
        await client.EnableOnDemandSandboxActivitiesAsync();

        // Assert
        OnDemandSandboxActivityDeclaration declaration = callInvoker.DeclareRequests
            .Should()
            .ContainSingle(request => request.WorkerProfileId == "client-test-profile")
            .Subject;
        declaration.ActivityNames.Should().Equal("ClientTestRemoteActivity");
        declaration.Image.ImageRef.Should().Be("example.com/client-test-worker:latest");
        callInvoker.DeclareHeaders.Should().Contain(header => header.Key == "taskhub" && header.Value == "client-test-taskhub");
        callInvoker.UnaryDisposeCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RemoveOnDemandSandboxActivityDeclarationAsync_SendsRequest()
    {
        // Arrange
        RecordingOnDemandSandboxLogCallInvoker callInvoker = new();
        OnDemandSandboxActivities.OnDemandSandboxActivitiesClient client = new(callInvoker);

        // Act
        await client.RemoveOnDemandSandboxActivityDeclarationAsync("default");

        // Assert
        callInvoker.RemoveRequest.Should().NotBeNull();
        callInvoker.RemoveRequest!.WorkerProfileId.Should().Be("default");
        callInvoker.RemoveHeaders.Should().NotContain(header => header.Key == "taskhub");
        callInvoker.UnaryDisposeCount.Should().Be(1);
    }

    sealed class RecordingOnDemandSandboxLogCallInvoker : CallInvoker
    {
        public List<OnDemandSandboxActivityDeclaration> DeclareRequests { get; } = [];

        public Metadata DeclareHeaders { get; private set; } = [];

        public RemoveOnDemandSandboxActivityDeclarationRequest? RemoveRequest { get; private set; }

        public Metadata RemoveHeaders { get; private set; } = [];

        public int UnaryDisposeCount { get; private set; }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request)
        {
            throw new NotSupportedException();
        }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request)
        {
            if (method.FullName.EndsWith("/DeclareOnDemandSandboxActivities", StringComparison.Ordinal))
            {
                this.DeclareRequests.Add(((OnDemandSandboxActivityDeclaration)(object)request).Clone());
                this.DeclareHeaders = options.Headers ?? [];
                return CreateUnaryCall((TResponse)(object)new OnDemandSandboxActivityDeclarationResult());
            }

            if (method.FullName.EndsWith("/RemoveOnDemandSandboxActivityDeclaration", StringComparison.Ordinal))
            {
                this.RemoveRequest = (RemoveOnDemandSandboxActivityDeclarationRequest)(object)request;
                this.RemoveHeaders = options.Headers ?? [];
                return CreateUnaryCall((TResponse)(object)new RemoveOnDemandSandboxActivityDeclarationResult());
            }

            throw new NotSupportedException(method.FullName);
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request)
        {
            throw new NotSupportedException();
        }

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options)
        {
            throw new NotSupportedException();
        }

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options)
        {
            throw new NotSupportedException();
        }

        AsyncUnaryCall<TResponse> CreateUnaryCall<TResponse>(TResponse response)
        {
            return new AsyncUnaryCall<TResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, string.Empty),
                () => new Metadata(),
                () => this.UnaryDisposeCount++);
        }
    }

    [OnDemandSandboxWorkerProfile("client-test-profile")]
    sealed class ClientTestWorkerProfile : ISandboxWorkerProfile
    {
        public void Configure(OnDemandSandboxOptions options)
        {
            options.ContainerImage = "example.com/client-test-worker:latest";
            options.ImagePullManagedIdentityClientId = "image-pull-client-id";
            options.SchedulerManagedIdentityClientId = "scheduler-client-id";
            options.Cpu = "500m";
            options.Memory = "1024Mi";
            options.MaxConcurrentActivities = 4;
            options.AddActivity("ClientTestRemoteActivity");
        }
    }
}
