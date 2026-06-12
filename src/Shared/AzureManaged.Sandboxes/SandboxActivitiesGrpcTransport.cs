// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Proto = Microsoft.DurableTask.Protobuf.Sandboxes;

namespace Microsoft.DurableTask.AzureManaged.Internal;

/// <summary>
/// Transport abstraction for the on-demand sandbox activities gRPC service.
/// </summary>
interface ISandboxActivitiesTransport
{
    /// <summary>
    /// Declares on-demand sandbox activities to DTS.
    /// </summary>
    /// <param name="declaration">The declaration message.</param>
    /// <param name="taskHub">The task hub that owns the declaration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The declaration result.</returns>
    Task<Proto.SandboxActivityDeclarationResult> DeclareSandboxActivitiesAsync(
        Proto.SandboxActivityDeclaration declaration,
        string taskHub,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes an on-demand sandbox activity declaration from DTS.
    /// </summary>
    /// <param name="workerProfileId">The worker profile ID whose declaration should be removed.</param>
    /// <param name="taskHub">The task hub that owns the declaration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The removal result.</returns>
    Task<Proto.RemoveSandboxActivityDeclarationResult> RemoveSandboxActivityDeclarationAsync(
        string workerProfileId,
        string taskHub,
        CancellationToken cancellationToken);

    /// <summary>
    /// Opens an on-demand sandbox activity worker registration session.
    /// </summary>
    /// <param name="taskHub">The task hub that owns the worker session.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The worker registration session.</returns>
    ISandboxActivityWorkerSession OpenSandboxActivityWorkerSession(string taskHub, CancellationToken cancellationToken);
}

/// <summary>
/// Client-streaming session used by an on-demand sandbox activity worker registration.
/// </summary>
interface ISandboxActivityWorkerSession : IAsyncDisposable
{
    /// <summary>
    /// Writes a worker registration message to the stream.
    /// </summary>
    /// <param name="message">The message to write.</param>
    /// <returns>A task that completes when the message is written.</returns>
    Task WriteMessageAsync(Proto.SandboxActivityWorkerMessage message);

    /// <summary>
    /// Waits for the server to complete the worker registration session.
    /// </summary>
    /// <returns>The worker session result.</returns>
    Task<Proto.SandboxActivityWorkerSessionResult> WaitForCompletionAsync();

    /// <summary>
    /// Completes the request stream and waits for the server response.
    /// </summary>
    /// <returns>A task that completes when the server response is observed.</returns>
    Task CompleteAsync();
}

/// <summary>
/// gRPC-backed implementation of <see cref="ISandboxActivitiesTransport"/>.
/// </summary>
sealed class SandboxActivitiesGrpcTransport : ISandboxActivitiesTransport
{
    readonly Proto.SandboxActivities.SandboxActivitiesClient client;
    readonly bool attachTaskHubMetadata;

    /// <summary>
    /// Initializes a new instance of the <see cref="SandboxActivitiesGrpcTransport"/> class.
    /// </summary>
    /// <param name="client">The generated on-demand sandbox activities gRPC client.</param>
    /// <param name="attachTaskHubMetadata">True to add per-call task hub metadata when the underlying channel does not already do so.</param>
    public SandboxActivitiesGrpcTransport(
        Proto.SandboxActivities.SandboxActivitiesClient client,
        bool attachTaskHubMetadata = true)
    {
        this.client = Check.NotNull(client);
        this.attachTaskHubMetadata = attachTaskHubMetadata;
    }

    /// <inheritdoc/>
    public async Task<Proto.SandboxActivityDeclarationResult> DeclareSandboxActivitiesAsync(
        Proto.SandboxActivityDeclaration declaration,
        string taskHub,
        CancellationToken cancellationToken)
    {
        using AsyncUnaryCall<Proto.SandboxActivityDeclarationResult> call =
            this.client.DeclareSandboxActivitiesAsync(
                declaration,
                headers: this.CreateTaskHubHeaders(taskHub),
                cancellationToken: cancellationToken);
        return await call.ResponseAsync.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Proto.RemoveSandboxActivityDeclarationResult> RemoveSandboxActivityDeclarationAsync(
        string workerProfileId,
        string taskHub,
        CancellationToken cancellationToken)
    {
        Proto.RemoveSandboxActivityDeclarationRequest request = new()
        {
            WorkerProfileId = workerProfileId,
        };

        using AsyncUnaryCall<Proto.RemoveSandboxActivityDeclarationResult> call =
            this.client.RemoveSandboxActivityDeclarationAsync(
                request,
                headers: this.CreateTaskHubHeaders(taskHub),
                cancellationToken: cancellationToken);
        return await call.ResponseAsync.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ISandboxActivityWorkerSession OpenSandboxActivityWorkerSession(string taskHub, CancellationToken cancellationToken)
    {
        AsyncClientStreamingCall<Proto.SandboxActivityWorkerMessage, Proto.SandboxActivityWorkerSessionResult> call =
            this.client.ConnectSandboxActivityWorker(
                headers: this.CreateTaskHubHeaders(taskHub),
                cancellationToken: cancellationToken);
        return new GrpcSandboxActivityWorkerSession(call);
    }

    Metadata? CreateTaskHubHeaders(string taskHub) => this.attachTaskHubMetadata
        ? new Metadata { { "taskhub", taskHub }, }
        : null;

    /// <summary>
    /// gRPC-backed on-demand sandbox activity worker registration session.
    /// </summary>
    sealed class GrpcSandboxActivityWorkerSession : ISandboxActivityWorkerSession
    {
        readonly AsyncClientStreamingCall<Proto.SandboxActivityWorkerMessage, Proto.SandboxActivityWorkerSessionResult> call;

        /// <summary>
        /// Initializes a new instance of the <see cref="GrpcSandboxActivityWorkerSession"/> class.
        /// </summary>
        /// <param name="call">The active gRPC client-streaming call.</param>
        public GrpcSandboxActivityWorkerSession(AsyncClientStreamingCall<Proto.SandboxActivityWorkerMessage, Proto.SandboxActivityWorkerSessionResult> call)
        {
            this.call = call;
        }

        /// <inheritdoc/>
        public Task WriteMessageAsync(Proto.SandboxActivityWorkerMessage message) =>
            this.call.RequestStream.WriteAsync(message);

        /// <inheritdoc/>
        public async Task<Proto.SandboxActivityWorkerSessionResult> WaitForCompletionAsync() =>
            await this.call.ResponseAsync.ConfigureAwait(false);

        /// <inheritdoc/>
        public async Task CompleteAsync()
        {
            await this.call.RequestStream.CompleteAsync().ConfigureAwait(false);
            await this.WaitForCompletionAsync().ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            this.call.Dispose();
            return default;
        }
    }
}
