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
    /// Lists DTS-managed sandboxes for a serverless activity worker profile using task hub metadata already configured on the gRPC channel.
    /// </summary>
    /// <param name="client">The generated serverless activities gRPC client.</param>
    /// <param name="workerProfileId">The worker profile ID to list sandboxes for.</param>
    /// <param name="cancellation">The cancellation token used to cancel the request.</param>
    /// <returns>The sandboxes currently known to DTS for the worker profile.</returns>
    public static Task<IReadOnlyList<ServerlessSandboxInfo>> ListServerlessActivitySandboxesAsync(
        this Proto.ServerlessActivities.ServerlessActivitiesClient client,
        string workerProfileId,
        CancellationToken cancellation = default)
    {
        return ListServerlessActivitySandboxesCoreAsync(
            client,
            workerProfileId,
            cancellation);
    }

    /// <summary>
    /// Removes a serverless activity declaration for a worker profile using task hub metadata already configured on the gRPC channel.
    /// </summary>
    /// <param name="client">The generated serverless activities gRPC client.</param>
    /// <param name="workerProfileId">The worker profile ID whose declaration should be removed.</param>
    /// <param name="cancellation">The cancellation token used to cancel the request.</param>
    /// <returns>A task that completes when DTS removes the declaration.</returns>
    public static Task RemoveServerlessActivityDeclarationAsync(
        this Proto.ServerlessActivities.ServerlessActivitiesClient client,
        string workerProfileId,
        CancellationToken cancellation = default)
    {
        return RemoveServerlessActivityDeclarationCoreAsync(
            client,
            workerProfileId,
            cancellation);
    }

    /// <summary>
    /// Streams logs from a serverless activity sandbox using task hub metadata already configured on the gRPC channel.
    /// </summary>
    /// <param name="client">The generated serverless activities gRPC client.</param>
    /// <param name="dtsSandboxIdentifier">The DTS sandbox identifier to stream logs from.</param>
    /// <param name="tail">The number of historical log lines to include before streaming live logs. Must be between 0 and 300.</param>
    /// <param name="cancellation">The cancellation token used to stop streaming.</param>
    /// <returns>An async stream of sandbox log lines.</returns>
    public static IAsyncEnumerable<ServerlessSandboxLogLine> StreamSandboxLogsAsync(
        this Proto.ServerlessActivities.ServerlessActivitiesClient client,
        string dtsSandboxIdentifier,
        int tail = 100,
        CancellationToken cancellation = default)
    {
        return StreamSandboxLogsCoreAsync(
            client,
            dtsSandboxIdentifier,
            tail,
            cancellation);
    }

    static async Task<IReadOnlyList<ServerlessSandboxInfo>> ListServerlessActivitySandboxesCoreAsync(
        Proto.ServerlessActivities.ServerlessActivitiesClient client,
        string workerProfileId,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(client);
        ValidateRequired(workerProfileId, nameof(workerProfileId), "Worker profile ID is required.");

        Proto.ListServerlessActivitySandboxesRequest request = new()
        {
            WorkerProfileId = workerProfileId,
        };

        using AsyncUnaryCall<Proto.ListServerlessActivitySandboxesResult> call = client.ListServerlessActivitySandboxesAsync(
            request,
            headers: null,
            cancellationToken: cancellation);
        Proto.ListServerlessActivitySandboxesResult result = await call.ResponseAsync.ConfigureAwait(false);

        List<ServerlessSandboxInfo> sandboxes = new(result.Sandboxes.Count);
        foreach (Proto.ServerlessActivitySandbox sandbox in result.Sandboxes)
        {
            sandboxes.Add(FromProto(sandbox));
        }

        return sandboxes;
    }

    static async Task RemoveServerlessActivityDeclarationCoreAsync(
        Proto.ServerlessActivities.ServerlessActivitiesClient client,
        string workerProfileId,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(client);
        ValidateRequired(workerProfileId, nameof(workerProfileId), "Worker profile ID is required.");

        Proto.RemoveServerlessActivityDeclarationRequest request = new()
        {
            WorkerProfileId = workerProfileId,
        };

        using AsyncUnaryCall<Proto.RemoveServerlessActivityDeclarationResult> call = client.RemoveServerlessActivityDeclarationAsync(
            request,
            headers: null,
            cancellationToken: cancellation);
        await call.ResponseAsync.ConfigureAwait(false);
    }

    static async IAsyncEnumerable<ServerlessSandboxLogLine> StreamSandboxLogsCoreAsync(
        Proto.ServerlessActivities.ServerlessActivitiesClient client,
        string dtsSandboxIdentifier,
        int tail,
        [EnumeratorCancellation] CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(client);
        ValidateRequest(dtsSandboxIdentifier, tail);

        Proto.SandboxLogStreamRequest request = new()
        {
            DtsSandboxIdentifier = dtsSandboxIdentifier,
            Tail = tail,
        };

        using AsyncServerStreamingCall<Proto.SandboxLogLine> call = client.StreamSandboxLogs(
            request,
            headers: null,
            cancellationToken: cancellation);

        while (await call.ResponseStream.MoveNext(cancellation).ConfigureAwait(false))
        {
            yield return FromProto(call.ResponseStream.Current);
        }
    }

    static void ValidateRequest(string dtsSandboxIdentifier, int tail)
    {
        ValidateRequired(
            dtsSandboxIdentifier,
            nameof(dtsSandboxIdentifier),
            "DTS sandbox identifier is required.");

        if (tail < MinTail || tail > MaxTail)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tail),
                tail,
                $"Tail must be between {MinTail} and {MaxTail}.");
        }
    }

    static void ValidateRequired(string value, string parameterName, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(message, parameterName);
        }
    }

    static ServerlessSandboxInfo FromProto(Proto.ServerlessActivitySandbox sandbox) => new(
        sandbox.DtsSandboxIdentifier,
        sandbox.WorkerProfileId,
        sandbox.CreatedAt?.ToDateTimeOffset() ?? default,
        sandbox.State);

    static ServerlessSandboxLogLine FromProto(Proto.SandboxLogLine line) => new(
        line.DtsSandboxIdentifier,
        line.Timestamp?.ToDateTimeOffset() ?? default,
        line.Stream,
        line.Tag,
        line.Message,
        line.RawLine);
}
