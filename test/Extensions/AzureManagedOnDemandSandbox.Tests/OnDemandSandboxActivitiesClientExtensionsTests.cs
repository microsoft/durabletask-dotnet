// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Grpc.Core;
using Microsoft.DurableTask.Client.Grpc;
using Microsoft.DurableTask.Protobuf.OnDemandSandbox;
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
            method.FullName.Should().EndWith("/RemoveOnDemandSandboxActivityDeclaration");
            this.RemoveRequest = (RemoveOnDemandSandboxActivityDeclarationRequest)(object)request;
            this.RemoveHeaders = options.Headers ?? [];

            return new AsyncUnaryCall<TResponse>(
                Task.FromResult((TResponse)(object)new RemoveOnDemandSandboxActivityDeclarationResult()),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, string.Empty),
                () => new Metadata(),
                () => this.UnaryDisposeCount++);
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
    }
}
