// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Proto = Microsoft.DurableTask.Protobuf.Serverless;

namespace Microsoft.DurableTask.Client.AzureManaged;

/// <summary>
/// Extension methods for the generated serverless activities gRPC client.
/// </summary>
public static class ServerlessActivitiesClientExtensions
{
    const int MinTail = 0;
    const int MaxTail = 300;

    /// <summary>
    /// Streams logs from a serverless activity sandbox using task hub metadata already configured on the gRPC channel.
    /// </summary>
    /// <param name="client">The generated serverless activities gRPC client.</param>
    /// <param name="sandboxId">The sandbox ID to stream logs from.</param>
    /// <param name="tail">The number of historical log lines to include before streaming live logs. Must be between 0 and 300.</param>
    /// <param name="cancellation">The cancellation token used to stop streaming.</param>
    /// <returns>An async stream of sandbox log lines.</returns>
    public static IAsyncEnumerable<ServerlessSandboxLogLine> StreamSandboxLogsAsync(
        this Proto.ServerlessActivities.ServerlessActivitiesClient client,
        string sandboxId,
        int tail = 100,
        CancellationToken cancellation = default)
    {
        return StreamSandboxLogsCoreAsync(
            client,
            sandboxId,
            taskHub: null,
            tail,
            cancellation);
    }

    /// <summary>
    /// Streams logs from a serverless activity sandbox with explicit task hub metadata.
    /// </summary>
    /// <param name="client">The generated serverless activities gRPC client.</param>
    /// <param name="sandboxId">The sandbox ID to stream logs from.</param>
    /// <param name="taskHub">The task hub that owns the sandbox.</param>
    /// <param name="tail">The number of historical log lines to include before streaming live logs. Must be between 0 and 300.</param>
    /// <param name="cancellation">The cancellation token used to stop streaming.</param>
    /// <returns>An async stream of sandbox log lines.</returns>
    public static IAsyncEnumerable<ServerlessSandboxLogLine> StreamSandboxLogsAsync(
        this Proto.ServerlessActivities.ServerlessActivitiesClient client,
        string sandboxId,
        string taskHub,
        int tail = 100,
        CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(taskHub))
        {
            throw new ArgumentException("Task hub name is required.", nameof(taskHub));
        }

        return StreamSandboxLogsCoreAsync(
            client,
            sandboxId,
            taskHub,
            tail,
            cancellation);
    }

    static async IAsyncEnumerable<ServerlessSandboxLogLine> StreamSandboxLogsCoreAsync(
        Proto.ServerlessActivities.ServerlessActivitiesClient client,
        string sandboxId,
        string? taskHub,
        int tail,
        [EnumeratorCancellation] CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(client);
        ValidateRequest(sandboxId, tail);

        Proto.SandboxLogStreamRequest request = new()
        {
            SandboxId = sandboxId,
            Tail = tail,
        };

        Metadata? headers = taskHub is null ? null : new Metadata { { "taskhub", taskHub } };
        using AsyncServerStreamingCall<Proto.SandboxLogLine> call = client.StreamSandboxLogs(
            request,
            headers: headers,
            cancellationToken: cancellation);

        while (await call.ResponseStream.MoveNext(cancellation).ConfigureAwait(false))
        {
            yield return FromProto(call.ResponseStream.Current);
        }
    }

    static void ValidateRequest(string sandboxId, int tail)
    {
        if (string.IsNullOrWhiteSpace(sandboxId))
        {
            throw new ArgumentException("Sandbox ID is required.", nameof(sandboxId));
        }

        if (tail < MinTail || tail > MaxTail)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tail),
                tail,
                $"Tail must be between {MinTail} and {MaxTail}.");
        }
    }

    static ServerlessSandboxLogLine FromProto(Proto.SandboxLogLine line) => new(
        line.SandboxId,
        line.Timestamp?.ToDateTimeOffset() ?? default,
        line.Stream,
        line.Tag,
        line.Message,
        line.RawLine);
}
