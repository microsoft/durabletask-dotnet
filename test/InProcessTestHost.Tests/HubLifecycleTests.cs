// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Grpc.Core;
using Microsoft.DurableTask.Testing.Sidecar.Grpc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using P = Microsoft.DurableTask.Protobuf;

namespace InProcessTestHost.Tests;

/// <summary>
/// Tests that task hub lifecycle responses reflect completion of their backend operations.
/// </summary>
public class HubLifecycleTests
{
    /// <summary>
    /// Returns an empty response only after creation completes, preserving the recreation option.
    /// </summary>
    /// <param name="recreateIfExists">Whether to recreate an existing hub.</param>
    /// <param name="alreadyCompleted">Whether creation completes before the call.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CreateTaskHub_BackendCompletion_ReturnsEmptyResponseAsync(bool recreateIfExists, bool alreadyCompleted)
    {
        // Arrange
        TaskCompletionSource backendCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (alreadyCompleted)
        {
            backendCompletion.SetResult();
        }

        Mock<IOrchestrationService> service = new();
        service.Setup(s => s.CreateAsync(recreateIfExists)).Returns(backendCompletion.Task);
        using TaskHubGrpcServer server = CreateServer(service.Object);

        // Act
        Task<P.CreateTaskHubResponse> responseTask = server.CreateTaskHub(
            new() { RecreateIfExists = recreateIfExists }, Mock.Of<ServerCallContext>());

        // Assert
        try
        {
            if (!alreadyCompleted)
            {
                Assert.False(responseTask.IsCompleted);
            }
        }
        finally
        {
            backendCompletion.TrySetResult();
        }

        P.CreateTaskHubResponse response = await responseTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new P.CreateTaskHubResponse(), response);
        service.Verify(s => s.CreateAsync(recreateIfExists), Times.Once);
    }

    /// <summary>
    /// Returns an empty response only after deletion completes.
    /// </summary>
    /// <param name="alreadyCompleted">Whether deletion completes before the call.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteTaskHub_BackendCompletion_ReturnsEmptyResponseAsync(bool alreadyCompleted)
    {
        // Arrange
        TaskCompletionSource backendCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (alreadyCompleted)
        {
            backendCompletion.SetResult();
        }

        Mock<IOrchestrationService> service = new();
        service.Setup(s => s.DeleteAsync()).Returns(backendCompletion.Task);
        using TaskHubGrpcServer server = CreateServer(service.Object);

        // Act
        Task<P.DeleteTaskHubResponse> responseTask = server.DeleteTaskHub(new(), Mock.Of<ServerCallContext>());

        // Assert
        try
        {
            if (!alreadyCompleted)
            {
                Assert.False(responseTask.IsCompleted);
            }
        }
        finally
        {
            backendCompletion.TrySetResult();
        }

        P.DeleteTaskHubResponse response = await responseTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new P.DeleteTaskHubResponse(), response);
        service.Verify(s => s.DeleteAsync(), Times.Once);
    }

    /// <summary>
    /// Propagates the original creation failure whether it occurs before or after the call.
    /// </summary>
    /// <param name="recreateIfExists">Whether to recreate an existing hub.</param>
    /// <param name="alreadyFaulted">Whether creation fails before the call.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CreateTaskHub_BackendFailure_PropagatesExceptionAsync(bool recreateIfExists, bool alreadyFaulted)
    {
        // Arrange
        InvalidOperationException expected = new("Backend creation failed.");
        TaskCompletionSource backendCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (alreadyFaulted)
        {
            backendCompletion.SetException(expected);
        }

        Mock<IOrchestrationService> service = new();
        service.Setup(s => s.CreateAsync(recreateIfExists)).Returns(backendCompletion.Task);
        using TaskHubGrpcServer server = CreateServer(service.Object);

        // Act
        Task<P.CreateTaskHubResponse> responseTask = server.CreateTaskHub(
            new() { RecreateIfExists = recreateIfExists }, Mock.Of<ServerCallContext>());
        backendCompletion.TrySetException(expected);

        // Assert
        try
        {
            InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
                () => responseTask.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Same(expected, actual);
            service.Verify(s => s.CreateAsync(recreateIfExists), Times.Once);
        }
        finally
        {
            // Observe the backend fault even if the RPC incorrectly returns success.
            _ = backendCompletion.Task.Exception;
        }
    }

    /// <summary>
    /// Propagates the original deletion failure whether it occurs before or after the call.
    /// </summary>
    /// <param name="alreadyFaulted">Whether deletion fails before the call.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteTaskHub_BackendFailure_PropagatesExceptionAsync(bool alreadyFaulted)
    {
        // Arrange
        InvalidOperationException expected = new("Backend deletion failed.");
        TaskCompletionSource backendCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (alreadyFaulted)
        {
            backendCompletion.SetException(expected);
        }

        Mock<IOrchestrationService> service = new();
        service.Setup(s => s.DeleteAsync()).Returns(backendCompletion.Task);
        using TaskHubGrpcServer server = CreateServer(service.Object);

        // Act
        Task<P.DeleteTaskHubResponse> responseTask = server.DeleteTaskHub(new(), Mock.Of<ServerCallContext>());
        backendCompletion.TrySetException(expected);

        // Assert
        try
        {
            InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
                () => responseTask.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Same(expected, actual);
            service.Verify(s => s.DeleteAsync(), Times.Once);
        }
        finally
        {
            // Observe the backend fault even if the RPC incorrectly returns success.
            _ = backendCompletion.Task.Exception;
        }
    }

    static TaskHubGrpcServer CreateServer(IOrchestrationService service) => new(
        Mock.Of<IHostApplicationLifetime>(), NullLoggerFactory.Instance, service,
        Mock.Of<IOrchestrationServiceClient>(), Options.Create(new TaskHubGrpcServerOptions()));
}
