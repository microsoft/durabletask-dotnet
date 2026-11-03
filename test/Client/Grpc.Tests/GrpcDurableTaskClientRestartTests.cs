// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Client.Grpc.Tests;

public class GrpcDurableTaskClientRestartTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v2")]
    public async Task RestartAsync_Options_ForwardsExplicitTargetAndVersion(string? version)
    {
        // Arrange
        using CancellationTokenSource cancellation = new();
        P.RestartInstanceRequest? captured = null;
        CancellationToken capturedCancellation = default;
        Mock<CallInvoker> invoker = CreateInvoker((request, callOptions) =>
        {
            captured = P.RestartInstanceRequest.Parser.ParseFrom(request.ToByteArray());
            capturedCancellation = callOptions.CancellationToken;
            return Task.FromResult(new P.RestartInstanceResponse { InstanceId = "target" });
        });
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        string result = await client.RestartAsync(
            "source", new RestartOrchestrationOptions { NewInstanceId = "target", Version = version }, cancellation.Token);

        // Assert
        result.Should().Be("target");
        captured.Should().NotBeNull();
        captured!.InstanceId.Should().Be("source");
        captured.NewInstanceId.Should().Be("target");
        captured.Version.Should().Be(version);
        captured.RestartWithNewInstanceId.Should().BeTrue();
        capturedCancellation.Should().Be(cancellation.Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestartAsync_LegacyBool_PreservesRequest(bool newInstanceId)
    {
        // Arrange
        P.RestartInstanceRequest? captured = null;
        Mock<CallInvoker> invoker = CreateInvoker((request, _) =>
        {
            captured = P.RestartInstanceRequest.Parser.ParseFrom(request.ToByteArray());
            return Task.FromResult(new P.RestartInstanceResponse { InstanceId = "returned-id" });
        });
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        string result = await client.RestartAsync("source", newInstanceId);

        // Assert
        result.Should().Be("returned-id");
        captured!.InstanceId.Should().Be("source");
        captured.RestartWithNewInstanceId.Should().Be(newInstanceId);
        captured.NewInstanceId.Should().BeNull();
        captured.Version.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task RestartAsync_InvalidTarget_DoesNotSendRequest(string? target)
    {
        // Arrange
        Mock<CallInvoker> invoker = new(MockBehavior.Strict);
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        Func<Task> act = () => client.RestartAsync("source", new RestartOrchestrationOptions { NewInstanceId = target! });

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
        invoker.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task RestartAsync_InvalidSource_DoesNotSendRequest(string? source)
    {
        // Arrange
        Mock<CallInvoker> invoker = new(MockBehavior.Strict);
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        Func<Task> act = () => client.RestartAsync(source!, new RestartOrchestrationOptions { NewInstanceId = "target" });

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
        invoker.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RestartAsync_NullOptions_DoesNotSendRequest()
    {
        // Arrange
        Mock<CallInvoker> invoker = new(MockBehavior.Strict);
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        Func<Task> act = () => client.RestartAsync("source", (RestartOrchestrationOptions)null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
        invoker.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("@counter@source", "target")]
    [InlineData("source", "@counter@target")]
    public async Task RestartAsync_EntityId_DoesNotSendRequest(string source, string target)
    {
        // Arrange
        Mock<CallInvoker> invoker = new(MockBehavior.Strict);
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        Func<Task> act = () => client.RestartAsync(source, new RestartOrchestrationOptions { NewInstanceId = target });

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
        invoker.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RestartAsync_ServerIgnoresExplicitTarget_ThrowsNotSupported()
    {
        // Arrange
        Mock<CallInvoker> invoker = CreateInvoker((_, _) =>
            Task.FromResult(new P.RestartInstanceResponse { InstanceId = "generated-id" }));
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        Func<Task> act = () => client.RestartAsync("source", new RestartOrchestrationOptions { NewInstanceId = "target" });

        // Assert
        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*legacy restart may already have occurred*");
    }

    [Theory]
    [InlineData(StatusCode.NotFound, typeof(ArgumentException))]
    [InlineData(StatusCode.FailedPrecondition, typeof(InvalidOperationException))]
    [InlineData(StatusCode.Cancelled, typeof(OperationCanceledException))]
    [InlineData(StatusCode.Unimplemented, typeof(RpcException))]
    public async Task RestartAsync_RpcError_PreservesErrorMapping(StatusCode status, Type expectedException)
    {
        // Arrange
        using CancellationTokenSource cancellation = new();
        Mock<CallInvoker> invoker = CreateInvoker((_, _) =>
            Task.FromException<P.RestartInstanceResponse>(new RpcException(new Status(status, "test failure"))));
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        Func<Task> act = () => client.RestartAsync(
            "source", new RestartOrchestrationOptions { NewInstanceId = "target" }, cancellation.Token);

        // Assert
        Exception exception = (await act.Should().ThrowAsync<Exception>()).Which;
        exception.GetType().Should().Be(expectedException);
        if (exception is OperationCanceledException canceled)
        {
            canceled.CancellationToken.Should().Be(cancellation.Token);
        }
    }

    [Fact]
    public void RestartAsync_BaseImplementation_DoesNotSilentlyIgnoreOptions()
    {
        // Arrange
        Mock<DurableTaskClient> client = new("test") { CallBase = true };

        // Act
        Action act = () => client.Object.RestartAsync("source", new RestartOrchestrationOptions { NewInstanceId = "target" });

        // Assert
        act.Should().Throw<NotSupportedException>();
    }

    static GrpcDurableTaskClient CreateClient(Mock<CallInvoker> invoker) =>
        new("test", new GrpcDurableTaskClientOptions { CallInvoker = invoker.Object, EnableEntitySupport = true },
            NullLogger.Instance);

    static Mock<CallInvoker> CreateInvoker(Func<P.RestartInstanceRequest, CallOptions, Task<P.RestartInstanceResponse>> response)
    {
        Mock<CallInvoker> invoker = new(MockBehavior.Strict);
        invoker.Setup(x => x.AsyncUnaryCall(
                It.IsAny<Method<P.RestartInstanceRequest, P.RestartInstanceResponse>>(),
                It.IsAny<string?>(), It.IsAny<CallOptions>(), It.IsAny<P.RestartInstanceRequest>()))
            .Returns((Method<P.RestartInstanceRequest, P.RestartInstanceResponse> _, string? _, CallOptions options,
                P.RestartInstanceRequest request) =>
                new AsyncUnaryCall<P.RestartInstanceResponse>(
                    response(request, options), Task.FromResult(new Metadata()), () => Status.DefaultSuccess,
                    () => new Metadata(), () => { }));
        return invoker;
    }
}
