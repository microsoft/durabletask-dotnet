// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core.Exceptions;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Client.Grpc.Tests;

public class GrpcDurableTaskClientRestartTests
{
    [Theory]
    [InlineData("target", null)]
    [InlineData("target", "")]
    [InlineData("target", "v2")]
    [InlineData("source", null)]
    public async Task RestartAsync_ExplicitInstanceId_UsesCloneRpcAndForwardsTargetAndVersion(string target, string? version)
    {
        // Arrange
        using CancellationTokenSource cancellation = new();
        P.CloneInstanceRequest? captured = null;
        CancellationToken capturedCancellation = default;
        Mock<CallInvoker> invoker = CreateInvoker<P.CloneInstanceRequest, P.CloneInstanceResponse>((request, callOptions) =>
        {
            captured = P.CloneInstanceRequest.Parser.ParseFrom(request.ToByteArray());
            capturedCancellation = callOptions.CancellationToken;
            return Task.FromResult(new P.CloneInstanceResponse { InstanceId = target });
        });
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        string result = await client.RestartAsync(
            "source", target, version, cancellation.Token);

        // Assert
        result.Should().Be(target);
        captured.Should().NotBeNull();
        captured!.SourceInstanceId.Should().Be("source");
        captured.NewInstanceId.Should().Be(target);
        captured.Version.Should().Be(version);
        capturedCancellation.Should().Be(cancellation.Token);
        invoker.Verify(x => x.AsyncUnaryCall(
            It.Is<Method<P.CloneInstanceRequest, P.CloneInstanceResponse>>(method => method.Name == "CloneInstance"),
            It.IsAny<string?>(), It.IsAny<CallOptions>(), It.IsAny<P.CloneInstanceRequest>()), Times.Once);
        invoker.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestartAsync_LegacyDefaultArguments_UseRestartRpc(bool useBaseClient)
    {
        // Arrange
        using CancellationTokenSource cancellation = new();
        List<(P.RestartInstanceRequest Request, CancellationToken Cancellation)> captured = new();
        Mock<CallInvoker> invoker = CreateInvoker<P.RestartInstanceRequest, P.RestartInstanceResponse>((request, callOptions) =>
        {
            captured.Add((P.RestartInstanceRequest.Parser.ParseFrom(request.ToByteArray()), callOptions.CancellationToken));
            return Task.FromResult(new P.RestartInstanceResponse { InstanceId = "source" });
        });
        await using GrpcDurableTaskClient grpcClient = CreateClient(invoker);
        DurableTaskClient client = grpcClient;

        // Act
        string[] results = useBaseClient
            ? new[]
            {
                await client.RestartAsync("source"),
                await client.RestartAsync("source", default),
                await client.RestartAsync("source", default, default),
                await client.RestartAsync("source", default, cancellation.Token),
            }
            : new[]
            {
                await grpcClient.RestartAsync("source"),
                await grpcClient.RestartAsync("source", default),
                await grpcClient.RestartAsync("source", default, default),
                await grpcClient.RestartAsync("source", default, cancellation.Token),
            };

        // Assert
        results.Should().Equal("source", "source", "source", "source");
        captured.Should().HaveCount(4);
        captured.Should().OnlyContain(call => call.Request.InstanceId == "source" && !call.Request.RestartWithNewInstanceId);
        captured.Select(call => call.Cancellation).Should().Equal(default, default, default, cancellation.Token);
        invoker.Verify(x => x.AsyncUnaryCall(
            It.Is<Method<P.RestartInstanceRequest, P.RestartInstanceResponse>>(method => method.Name == "RestartInstance"),
            It.IsAny<string?>(), It.IsAny<CallOptions>(), It.IsAny<P.RestartInstanceRequest>()), Times.Exactly(4));
        invoker.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestartAsync_LegacyBool_PreservesRequest(bool newInstanceId)
    {
        // Arrange
        P.RestartInstanceRequest? captured = null;
        Mock<CallInvoker> invoker = CreateInvoker<P.RestartInstanceRequest, P.RestartInstanceResponse>((request, _) =>
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
        invoker.Verify(x => x.AsyncUnaryCall(
            It.Is<Method<P.RestartInstanceRequest, P.RestartInstanceResponse>>(method => method.Name == "RestartInstance"),
            It.IsAny<string?>(), It.IsAny<CallOptions>(), It.IsAny<P.RestartInstanceRequest>()), Times.Once);
        invoker.VerifyNoOtherCalls();
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
        Func<Task> act = () => client.RestartAsync("source", target!, null, default);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("newInstanceId");
        invoker.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task RestartAsync_InvalidSource_DoesNotSendRequest(string? source)
    {
        // Arrange
        Mock<CallInvoker> invoker = new(MockBehavior.Strict);
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        Func<Task> act = () => client.RestartAsync(source!, "target", null, default);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
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
        Func<Task> act = () => client.RestartAsync(source, target, null, default);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
        invoker.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RestartAsync_CloneResponseWithWrongTarget_ThrowsInvalidOperation()
    {
        // Arrange
        Mock<CallInvoker> invoker = CreateInvoker<P.CloneInstanceRequest, P.CloneInstanceResponse>((_, _) =>
            Task.FromResult(new P.CloneInstanceResponse { InstanceId = "unexpected-id" }));
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        Func<Task> act = () => client.RestartAsync("source", "target", null, default);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*unexpected-id*target*");
    }

    [Theory]
    [InlineData(StatusCode.NotFound, typeof(ArgumentException))]
    [InlineData(StatusCode.FailedPrecondition, typeof(InvalidOperationException))]
    [InlineData(StatusCode.InvalidArgument, typeof(ArgumentException))]
    [InlineData(StatusCode.AlreadyExists, typeof(OrchestrationAlreadyExistsException))]
    [InlineData(StatusCode.Cancelled, typeof(OperationCanceledException))]
    [InlineData(StatusCode.Unimplemented, typeof(NotSupportedException))]
    [InlineData(StatusCode.Unavailable, typeof(RpcException))]
    public async Task RestartAsync_ExplicitInstanceId_RpcError_MapsError(StatusCode status, Type expectedException)
    {
        // Arrange
        using CancellationTokenSource cancellation = new();
        Mock<CallInvoker> invoker = CreateInvoker<P.CloneInstanceRequest, P.CloneInstanceResponse>((_, _) =>
            Task.FromException<P.CloneInstanceResponse>(new RpcException(new Status(status, "test failure"))));
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        Func<Task> act = () => client.RestartAsync(
            "source", "target", null, cancellation.Token);

        // Assert
        Exception exception = (await act.Should().ThrowAsync<Exception>()).Which;
        exception.GetType().Should().Be(expectedException);
        if (exception is OperationCanceledException canceled)
        {
            canceled.CancellationToken.Should().Be(cancellation.Token);
        }
    }

    [Theory]
    [InlineData(StatusCode.NotFound, typeof(ArgumentException))]
    [InlineData(StatusCode.FailedPrecondition, typeof(InvalidOperationException))]
    [InlineData(StatusCode.Cancelled, typeof(OperationCanceledException))]
    [InlineData(StatusCode.Unimplemented, typeof(RpcException))]
    [InlineData(StatusCode.AlreadyExists, typeof(RpcException))]
    public async Task RestartAsync_LegacyBool_RpcError_PreservesErrorMapping(StatusCode status, Type expectedException)
    {
        // Arrange
        using CancellationTokenSource cancellation = new();
        Mock<CallInvoker> invoker = CreateInvoker<P.RestartInstanceRequest, P.RestartInstanceResponse>((_, _) =>
            Task.FromException<P.RestartInstanceResponse>(new RpcException(new Status(status, "test failure"))));
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        Func<Task> act = () => client.RestartAsync("source", true, cancellation.Token);

        // Assert
        Exception exception = (await act.Should().ThrowAsync<Exception>()).Which;
        exception.GetType().Should().Be(expectedException);
        if (exception is OperationCanceledException canceled)
        {
            canceled.CancellationToken.Should().Be(cancellation.Token);
        }
    }

    [Fact]
    public async Task RestartAsync_ExplicitInstanceId_UnsupportedServer_DoesNotFallbackToRestart()
    {
        // Arrange
        Mock<CallInvoker> invoker = CreateInvoker<P.CloneInstanceRequest, P.CloneInstanceResponse>((_, _) =>
            Task.FromException<P.CloneInstanceResponse>(
                new RpcException(new Status(StatusCode.Unimplemented, "unknown method"))));
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        Func<Task> act = () => client.RestartAsync("source", "target", null, default);

        // Assert
        NotSupportedException exception = (await act.Should().ThrowAsync<NotSupportedException>()).Which;
        exception.InnerException.Should().BeOfType<RpcException>()
            .Which.StatusCode.Should().Be(StatusCode.Unimplemented);
        invoker.Verify(x => x.AsyncUnaryCall(
            It.Is<Method<P.CloneInstanceRequest, P.CloneInstanceResponse>>(method => method.Name == "CloneInstance"),
            It.IsAny<string?>(), It.IsAny<CallOptions>(), It.IsAny<P.CloneInstanceRequest>()), Times.Once);
        invoker.VerifyNoOtherCalls();
    }

    [Fact]
    public void RestartAsync_BaseImplementation_DoesNotSilentlyIgnoreExplicitInstanceId()
    {
        // Arrange
        Mock<DurableTaskClient> client = new("test") { CallBase = true };

        // Act
        Action act = () => client.Object.RestartAsync("source", "target", null, default);

        // Assert
        act.Should().Throw<NotSupportedException>();
    }

    static GrpcDurableTaskClient CreateClient(Mock<CallInvoker> invoker) =>
        new("test", new GrpcDurableTaskClientOptions { CallInvoker = invoker.Object, EnableEntitySupport = true },
            NullLogger.Instance);

    static Mock<CallInvoker> CreateInvoker<TRequest, TResponse>(Func<TRequest, CallOptions, Task<TResponse>> response)
        where TRequest : class
        where TResponse : class
    {
        Mock<CallInvoker> invoker = new(MockBehavior.Strict);
        invoker.Setup(x => x.AsyncUnaryCall(
                It.IsAny<Method<TRequest, TResponse>>(),
                It.IsAny<string?>(), It.IsAny<CallOptions>(), It.IsAny<TRequest>()))
            .Returns((Method<TRequest, TResponse> _, string? _, CallOptions options, TRequest request) =>
                new AsyncUnaryCall<TResponse>(
                    response(request, options), Task.FromResult(new Metadata()), () => Status.DefaultSuccess,
                    () => new Metadata(), () => { }));
        return invoker;
    }
}
