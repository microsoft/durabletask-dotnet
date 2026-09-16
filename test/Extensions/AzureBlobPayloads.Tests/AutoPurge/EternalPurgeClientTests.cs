// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Grpc;
using Microsoft.Extensions.Logging.Abstractions;
using P = Microsoft.DurableTask.Protobuf;
using LP = Microsoft.DurableTask.Protobuf.LargePayloads;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests.AutoPurge;

/// <summary>
/// Exercises the public control API through its actual gRPC client, with entities disabled.
/// </summary>
public class EternalPurgeClientTests
{
    const string RunnerId = "BlobPurgeJob-__dt_blob_payload_autopurge__";
    const string RunnerName = "BlobPurgeJobOrchestrator";

    [Fact]
    public async Task Disable_OnlyWritesBackendSettingAsync()
    {
        // Arrange
        RecordingInvoker invoker = new();
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        await client.SetLargePayloadAutoPurgeAsync(false, batchSize: -1);

        // Assert
        Assert.Equal(new[] { "SetLargePayloadAutoPurge" }, invoker.Methods);
        Assert.False(Assert.Single(invoker.Sets).Enabled);
    }

    [Fact]
    public async Task Enable_WaitsForActualStart_ThenConfiguresAsync()
    {
        // Arrange
        TaskCompletionSource<P.GetInstanceResponse> start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingInvoker invoker = new() { StartResponse = start.Task };
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        Task enable = client.SetLargePayloadAutoPurgeAsync(true, 42);
        await invoker.WaitRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(enable.IsCompleted);
        Assert.Empty(invoker.Events);
        start.SetResult(State(P.OrchestrationStatus.Running));
        await enable;

        // Assert
        Assert.Equal(new[] { "SetLargePayloadAutoPurge", "StartInstance", "WaitForInstanceStart", "RaiseEvent" }, invoker.Methods);
        P.CreateInstanceRequest create = Assert.Single(invoker.Starts);
        Assert.Equal(RunnerId, create.InstanceId);
        Assert.Equal(RunnerName, create.Name);
        Assert.Contains("\"PurgeBatchSize\":42", create.Input);
#pragma warning disable CS0618
        Assert.Equal(new[] { P.OrchestrationStatus.Completed, P.OrchestrationStatus.Failed, P.OrchestrationStatus.Terminated, P.OrchestrationStatus.Canceled },
            create.OrchestrationIdReusePolicy.ReplaceableStatus);
#pragma warning restore CS0618
        P.RaiseEventRequest update = Assert.Single(invoker.Events);
        Assert.Equal(RunnerId, update.InstanceId);
        Assert.Equal("SetBatchSize", update.Name);
        Assert.Equal("42", update.Input);
    }

    [Theory]
    [InlineData(P.OrchestrationStatus.Running)]
    [InlineData(P.OrchestrationStatus.Pending)]
    public async Task Enable_ExistingLiveRunner_IsDeduplicatedAndWaitedForAsync(P.OrchestrationStatus status)
    {
        // Arrange
        TaskCompletionSource<P.GetInstanceResponse> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingInvoker invoker = new()
        {
            StartError = new RpcException(new Status(StatusCode.AlreadyExists, $"Existing runner is {status}")),
            StartResponse = started.Task,
        };
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        Task enable = client.SetLargePayloadAutoPurgeAsync(true, 333);
        await invoker.WaitRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(enable.IsCompleted);
        Assert.Empty(invoker.Events);
        started.SetResult(State(P.OrchestrationStatus.Running));
        await enable;

        // Assert
        Assert.DoesNotContain(status, Assert.Single(invoker.Starts).OrchestrationIdReusePolicy.ReplaceableStatus);
        Assert.Equal(new[] { "SetLargePayloadAutoPurge", "StartInstance", "WaitForInstanceStart", "RaiseEvent" }, invoker.Methods);
        Assert.Equal("333", Assert.Single(invoker.Events).Input);
    }

    [Theory]
    [InlineData(P.OrchestrationStatus.Completed)]
    [InlineData(P.OrchestrationStatus.Failed)]
    [InlineData(P.OrchestrationStatus.Terminated)]
#pragma warning disable CS0618
    [InlineData(P.OrchestrationStatus.Canceled)]
#pragma warning restore CS0618
    public async Task Enable_ExplicitPolicy_AllowsTerminalReplacementAsync(P.OrchestrationStatus status)
    {
        // Arrange
        RecordingInvoker invoker = new();
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        await client.SetLargePayloadAutoPurgeAsync(true);

        // Assert
        P.CreateInstanceRequest start = Assert.Single(invoker.Starts);
        Assert.Equal(RunnerId, start.InstanceId);
        Assert.Contains(status, start.OrchestrationIdReusePolicy.ReplaceableStatus);
        Assert.DoesNotContain(P.OrchestrationStatus.Running, start.OrchestrationIdReusePolicy.ReplaceableStatus);
        Assert.Single(invoker.Events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Enable_NameCollision_IsNotSuccessAsync(bool duringStartRace)
    {
        // Arrange
        RecordingInvoker invoker = new()
        {
            StartResponse = Task.FromResult(State(P.OrchestrationStatus.Running, "CustomerOrchestration")),
        };
        if (duringStartRace)
        {
            invoker.StartError = new RpcException(new Status(StatusCode.AlreadyExists, "racing start"));
        }
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        Func<Task> act = () => client.SetLargePayloadAutoPurgeAsync(true);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        Assert.Single(invoker.Starts);
        Assert.Empty(invoker.Events);
    }

    [Fact]
    public async Task Enable_AlreadyExistsRace_ValidatesAndWaitsAsync()
    {
        // Arrange
        RecordingInvoker invoker = new() { StartError = new RpcException(new Status(StatusCode.AlreadyExists, "race")) };
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        await client.SetLargePayloadAutoPurgeAsync(true);

        // Assert
        Assert.Single(invoker.Starts);
        Assert.True(invoker.WaitRequested.Task.IsCompleted);
        Assert.Single(invoker.Events);
    }

    [Theory]
    [InlineData(P.OrchestrationStatus.Failed)]
    [InlineData(P.OrchestrationStatus.Terminated)]
    [InlineData(P.OrchestrationStatus.Completed)]
    [InlineData(P.OrchestrationStatus.Suspended)]
    [InlineData(P.OrchestrationStatus.Pending)]
    [InlineData(P.OrchestrationStatus.ContinuedAsNew)]
    [InlineData((P.OrchestrationStatus)999)]
    public async Task Enable_WaitReturnsNotRunning_IsAnErrorAsync(P.OrchestrationStatus status)
    {
        // Arrange
        RecordingInvoker invoker = new()
        {
            StartResponse = Task.FromResult(State(status)),
            StartError = status == P.OrchestrationStatus.Suspended
                ? new RpcException(new Status(StatusCode.AlreadyExists, "suspended"))
                : null,
        };
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        Func<Task> act = () => client.SetLargePayloadAutoPurgeAsync(true);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        Assert.Single(invoker.Starts);
        Assert.Empty(invoker.Events);
    }

    [Fact]
    public async Task SequentialDisableEnable_UsesSameLiveRunner_AndUpdatesBatchAsync()
    {
        // Arrange
        RecordingInvoker invoker = new() { StartError = new RpcException(new Status(StatusCode.AlreadyExists, "running")) };
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act
        await client.SetLargePayloadAutoPurgeAsync(false);
        await client.SetLargePayloadAutoPurgeAsync(true, 123);
        await client.SetLargePayloadAutoPurgeAsync(true, 456);

        // Assert
        Assert.Equal(new[] { false, true, true }, invoker.Sets.Select(s => s.Enabled));
        Assert.Equal(2, invoker.Starts.Count);
        Assert.All(invoker.Starts, start => Assert.Equal(RunnerId, start.InstanceId));
        Assert.Equal(new[]
        {
            "SetLargePayloadAutoPurge",
            "SetLargePayloadAutoPurge", "StartInstance", "WaitForInstanceStart", "RaiseEvent",
            "SetLargePayloadAutoPurge", "StartInstance", "WaitForInstanceStart", "RaiseEvent",
        }, invoker.Methods);
        Assert.Equal(new[] { "123", "456" }, invoker.Events.Select(e => e.Input));
        Assert.All(invoker.Events, e => Assert.Equal(RunnerId, e.InstanceId));
        Assert.DoesNotContain(invoker.Methods, name => name.Contains("Entity") || name.Contains("Purge") && name != "SetLargePayloadAutoPurge"
            || name.Contains("Terminate") || name.Contains("Suspend"));
    }

    [Theory]
    [InlineData("SetLargePayloadAutoPurge", StatusCode.Unavailable)]
    [InlineData("StartInstance", StatusCode.Unavailable)]
    [InlineData("WaitForInstanceStart", StatusCode.Unavailable)]
    [InlineData("RaiseEvent", StatusCode.Unavailable)]
    [InlineData("SetLargePayloadAutoPurge", StatusCode.Cancelled)]
    [InlineData("StartInstance", StatusCode.Cancelled)]
    [InlineData("WaitForInstanceStart", StatusCode.Cancelled)]
    [InlineData("RaiseEvent", StatusCode.Cancelled)]
    public async Task Enable_FailureAtAnyStep_PropagatesWithoutRollbackAsync(string method, StatusCode status)
    {
        // Arrange
        RecordingInvoker invoker = new() { ErrorAt = method, ErrorStatus = status };
        await using GrpcDurableTaskClient client = CreateClient(invoker);
        using CancellationTokenSource cancellation = new();

        // Act
        Func<Task> act = () => client.SetLargePayloadAutoPurgeAsync(true, cancellationToken: cancellation.Token);

        // Assert
        if (status == StatusCode.Cancelled && method != "RaiseEvent")
        {
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            await act.Should().ThrowAsync<RpcException>().Where(error => error.StatusCode == status);
        }
        Assert.Single(invoker.Sets);
        Assert.True(invoker.Sets[0].Enabled);
        Assert.All(invoker.Tokens, token => Assert.Equal(cancellation.Token, token));
        Assert.Equal(method, invoker.Methods.Last());
    }

    [Fact]
    public async Task Enable_CallerCancellation_CancelsHeldStartWaitAsync()
    {
        // Arrange
        RecordingInvoker invoker = new() { StartResponse = new TaskCompletionSource<P.GetInstanceResponse>().Task };
        await using GrpcDurableTaskClient client = CreateClient(invoker);
        using CancellationTokenSource cancellation = new();

        // Act
        Task enable = client.SetLargePayloadAutoPurgeAsync(true, cancellationToken: cancellation.Token);
        await invoker.WaitRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => enable);
        Assert.Empty(invoker.Events);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public async Task Enable_InvalidBatch_DoesNotTouchBackendAsync(int size)
    {
        // Arrange
        RecordingInvoker invoker = new();
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.SetLargePayloadAutoPurgeAsync(true, size));
        Assert.Empty(invoker.Methods);
    }

    [Fact]
    public async Task NonGrpcClient_IsRejectedBeforeWorkAsync()
    {
        // Arrange
        Mock<DurableTaskClient> client = new(MockBehavior.Strict, "test");

        // Act / Assert
        await Assert.ThrowsAsync<NotSupportedException>(() => client.Object.SetLargePayloadAutoPurgeAsync(true));
        client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Enable_UnexpectedReturnedInstanceId_IsRejectedAsync()
    {
        // Arrange
        RecordingInvoker invoker = new() { StartInstanceId = "unexpected" };
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act / Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SetLargePayloadAutoPurgeAsync(true));
        Assert.Empty(invoker.Events);
    }

    [Fact]
    public async Task Enable_WaitReturnsUnexpectedInstanceId_IsRejectedAsync()
    {
        // Arrange
        RecordingInvoker invoker = new() { StartResponse = Task.FromResult(State(P.OrchestrationStatus.Running, id: "unexpected")) };
        await using GrpcDurableTaskClient client = CreateClient(invoker);

        // Act / Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SetLargePayloadAutoPurgeAsync(true));
        Assert.Single(invoker.Starts);
        Assert.Empty(invoker.Events);
    }

    static GrpcDurableTaskClient CreateClient(RecordingInvoker invoker) =>
        new("test", new GrpcDurableTaskClientOptions { CallInvoker = invoker, EnableEntitySupport = false }, NullLogger.Instance);

    static P.GetInstanceResponse State(P.OrchestrationStatus status, string name = RunnerName, string id = RunnerId) => new()
    {
        Exists = true,
        OrchestrationState = new P.OrchestrationState
        {
            InstanceId = id, Name = name, OrchestrationStatus = status,
            CreatedTimestamp = Timestamp.FromDateTime(DateTime.UnixEpoch),
            LastUpdatedTimestamp = Timestamp.FromDateTime(DateTime.UnixEpoch),
        },
    };

    sealed class RecordingInvoker : CallInvoker
    {
        public List<string> Methods { get; } = [];
        public List<LP.SetLargePayloadAutoPurgeRequest> Sets { get; } = [];
        public List<P.CreateInstanceRequest> Starts { get; } = [];
        public List<P.RaiseEventRequest> Events { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public RpcException? StartError { get; set; }
        public string? ErrorAt { get; init; }
        public StatusCode ErrorStatus { get; init; }
        public string StartInstanceId { get; init; } = RunnerId;
        public Task<P.GetInstanceResponse> StartResponse { get; set; } = Task.FromResult(State(P.OrchestrationStatus.Running));
        public TaskCompletionSource WaitRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            this.Methods.Add(method.Name);
            this.Tokens.Add(options.CancellationToken);
            Task<TResponse> result;
            switch (request)
            {
                case LP.SetLargePayloadAutoPurgeRequest set:
                    this.Sets.Add(set);
                    result = Task.FromResult((TResponse)(object)new LP.SetLargePayloadAutoPurgeResponse());
                    break;
                case P.GetInstanceRequest get:
                    Assert.Equal("WaitForInstanceStart", method.Name);
                    Assert.Equal(RunnerId, get.InstanceId);
                    Assert.False(get.GetInputsAndOutputs);
                    this.WaitRequested.TrySetResult();
                    result = ConvertAsync<TResponse>(this.StartResponse.WaitAsync(options.CancellationToken));
                    break;
                case P.CreateInstanceRequest create:
                    this.Starts.Add(create);
                    result = this.StartError is null
                        ? Task.FromResult((TResponse)(object)new P.CreateInstanceResponse { InstanceId = this.StartInstanceId })
                        : Task.FromException<TResponse>(this.StartError);
                    break;
                case P.RaiseEventRequest update:
                    this.Events.Add(update);
                    result = Task.FromResult((TResponse)(object)new P.RaiseEventResponse());
                    break;
                default:
                    throw new InvalidOperationException(method.Name);
            }
            if (method.Name == this.ErrorAt)
            {
                result = Task.FromException<TResponse>(new RpcException(new Status(this.ErrorStatus, "synthetic failure")));
            }
            return new(result, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
        }

        static async Task<T> ConvertAsync<T>(Task<P.GetInstanceResponse> task) => (T)(object)await task;
        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
    }
}
