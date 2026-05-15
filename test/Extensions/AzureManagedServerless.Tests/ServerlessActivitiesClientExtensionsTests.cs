// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.DurableTask.Protobuf.Serverless;
using Xunit;

namespace Microsoft.DurableTask.Client.AzureManaged.Tests;

public class ServerlessActivitiesClientExtensionsTests
{
    const string TaskHub = "testhub";

    [Fact]
    public async Task StreamSandboxLogsAsync_SendsRequestAndMapsLines()
    {
        // Arrange
        DateTimeOffset timestamp = new(2026, 5, 14, 10, 30, 0, TimeSpan.Zero);
        RecordingServerlessLogCallInvoker callInvoker = new(
            new SandboxLogLine
            {
                SandboxId = "sandbox-1",
                Timestamp = timestamp.ToTimestamp(),
                Stream = "stdout",
                Tag = "worker",
                Message = "hello from serverless",
                RawLine = "2026-05-14T10:30:00Z stdout worker hello from serverless",
            });
        ServerlessActivities.ServerlessActivitiesClient client = new(callInvoker);

        // Act
        List<ServerlessSandboxLogLine> lines = [];
        await foreach (ServerlessSandboxLogLine line in client.StreamSandboxLogsAsync(
            "sandbox-1",
            TaskHub,
            tail: 42))
        {
            lines.Add(line);
        }

        // Assert
        callInvoker.Request.Should().NotBeNull();
        callInvoker.Request!.SandboxId.Should().Be("sandbox-1");
        callInvoker.Request.Tail.Should().Be(42);
        callInvoker.Headers.Should().Contain(header => header.Key == "taskhub" && header.Value == TaskHub);
        callInvoker.DisposeCount.Should().Be(1);

        ServerlessSandboxLogLine mapped = lines.Should().ContainSingle().Subject;
        mapped.SandboxId.Should().Be("sandbox-1");
        mapped.Timestamp.Should().Be(timestamp);
        mapped.Stream.Should().Be("stdout");
        mapped.Tag.Should().Be("worker");
        mapped.Message.Should().Be("hello from serverless");
        mapped.RawLine.Should().Be("2026-05-14T10:30:00Z stdout worker hello from serverless");
    }

    [Fact]
    public async Task StreamSandboxLogsAsync_WithoutExplicitTaskHub_UsesConfiguredChannelMetadata()
    {
        // Arrange
        RecordingServerlessLogCallInvoker callInvoker = new();
        ServerlessActivities.ServerlessActivitiesClient client = new(callInvoker);

        // Act
        await foreach (ServerlessSandboxLogLine _ in client.StreamSandboxLogsAsync("sandbox-1", tail: 42))
        {
        }

        // Assert
        callInvoker.Headers.Should().NotContain(header => header.Key == "taskhub");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(301)]
    public async Task StreamSandboxLogsAsync_WithInvalidTail_ThrowsArgumentOutOfRangeException(int tail)
    {
        // Arrange
        ServerlessActivities.ServerlessActivitiesClient client = new(new RecordingServerlessLogCallInvoker());

        // Act
        Func<Task> action = async () =>
        {
            await foreach (ServerlessSandboxLogLine _ in client.StreamSandboxLogsAsync(
                "sandbox-1",
                TaskHub,
                tail))
            {
            }
        };

        // Assert
        await action.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithParameterName("tail");
    }

    sealed class RecordingServerlessLogCallInvoker : CallInvoker
    {
        readonly SandboxLogStreamReader responseStream;

        public RecordingServerlessLogCallInvoker(params SandboxLogLine[] lines)
        {
            this.responseStream = new SandboxLogStreamReader(lines);
        }

        public SandboxLogStreamRequest? Request { get; private set; }

        public Metadata Headers { get; private set; } = [];

        public int DisposeCount { get; private set; }

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
            throw new NotSupportedException();
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request)
        {
            method.FullName.Should().EndWith("/StreamSandboxLogs");
            this.Request = (SandboxLogStreamRequest)(object)request;
            this.Headers = options.Headers ?? [];

            return new AsyncServerStreamingCall<TResponse>(
                (IAsyncStreamReader<TResponse>)(object)this.responseStream,
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, string.Empty),
                () => new Metadata(),
                () => this.DisposeCount++);
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

    sealed class SandboxLogStreamReader : IAsyncStreamReader<SandboxLogLine>
    {
        readonly Queue<SandboxLogLine> lines;

        public SandboxLogStreamReader(IEnumerable<SandboxLogLine> lines)
        {
            this.lines = new Queue<SandboxLogLine>(lines);
        }

        public SandboxLogLine Current { get; private set; } = new();

        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (this.lines.Count == 0)
            {
                return Task.FromResult(false);
            }

            this.Current = this.lines.Dequeue();
            return Task.FromResult(true);
        }
    }
}
