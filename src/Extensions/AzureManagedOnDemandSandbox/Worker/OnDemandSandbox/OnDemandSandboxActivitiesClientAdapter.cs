// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Proto = Microsoft.DurableTask.Protobuf.OnDemandSandbox;

namespace Microsoft.DurableTask.Worker.AzureManaged.OnDemandSandbox;

/// <summary>
/// Client abstraction for the on-demand sandbox activities gRPC service.
/// </summary>
interface IOnDemandSandboxActivitiesClient
{
    /// <summary>
    /// Declares on-demand sandbox activities to DTS.
    /// </summary>
    /// <param name="declaration">The declaration message.</param>
    /// <param name="taskHub">The task hub that owns the declaration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The declaration result.</returns>
    Task<Proto.OnDemandSandboxActivityDeclarationResult> DeclareOnDemandSandboxActivitiesAsync(
        Proto.OnDemandSandboxActivityDeclaration declaration,
        string taskHub,
        CancellationToken cancellationToken);

    /// <summary>
    /// Opens an on-demand sandbox activity worker registration session.
    /// </summary>
    /// <param name="taskHub">The task hub that owns the worker session.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The worker registration session.</returns>
    IOnDemandSandboxActivityWorkerSession OpenOnDemandSandboxActivityWorkerSession(string taskHub, CancellationToken cancellationToken);
}

/// <summary>
/// Client-streaming session used by an on-demand sandbox activity worker registration.
/// </summary>
interface IOnDemandSandboxActivityWorkerSession : IAsyncDisposable
{
    /// <summary>
    /// Writes a worker registration message to the stream.
    /// </summary>
    /// <param name="message">The message to write.</param>
    /// <returns>A task that completes when the message is written.</returns>
    Task WriteMessageAsync(Proto.OnDemandSandboxActivityWorkerMessage message);

    /// <summary>
    /// Waits for the server to complete the worker registration session.
    /// </summary>
    /// <returns>The worker session result.</returns>
    Task<Proto.OnDemandSandboxActivityWorkerSessionResult> WaitForCompletionAsync();

    /// <summary>
    /// Completes the request stream and waits for the server response.
    /// </summary>
    /// <returns>A task that completes when the server response is observed.</returns>
    Task CompleteAsync();
}

/// <summary>
/// gRPC-backed implementation of <see cref="IOnDemandSandboxActivitiesClient"/>.
/// </summary>
sealed class OnDemandSandboxActivitiesClientAdapter : IOnDemandSandboxActivitiesClient
{
    readonly Proto.OnDemandSandboxActivities.OnDemandSandboxActivitiesClient client;
    readonly bool attachTaskHubMetadata;

    /// <summary>
    /// Initializes a new instance of the <see cref="OnDemandSandboxActivitiesClientAdapter"/> class.
    /// </summary>
    /// <param name="client">The generated on-demand sandbox activities gRPC client.</param>
    /// <param name="attachTaskHubMetadata">True to add per-call task hub metadata when the underlying channel does not already do so.</param>
    public OnDemandSandboxActivitiesClientAdapter(
        Proto.OnDemandSandboxActivities.OnDemandSandboxActivitiesClient client,
        bool attachTaskHubMetadata = true)
    {
        this.client = Check.NotNull(client);
        this.attachTaskHubMetadata = attachTaskHubMetadata;
    }

    /// <inheritdoc/>
    public async Task<Proto.OnDemandSandboxActivityDeclarationResult> DeclareOnDemandSandboxActivitiesAsync(
        Proto.OnDemandSandboxActivityDeclaration declaration,
        string taskHub,
        CancellationToken cancellationToken)
    {
        return await this.client.DeclareOnDemandSandboxActivitiesAsync(
                declaration,
                headers: this.CreateTaskHubHeaders(taskHub),
                cancellationToken: cancellationToken)
            .ResponseAsync.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public IOnDemandSandboxActivityWorkerSession OpenOnDemandSandboxActivityWorkerSession(string taskHub, CancellationToken cancellationToken)
    {
        AsyncClientStreamingCall<Proto.OnDemandSandboxActivityWorkerMessage, Proto.OnDemandSandboxActivityWorkerSessionResult> call =
            this.client.ConnectOnDemandSandboxActivityWorker(
                headers: this.CreateTaskHubHeaders(taskHub),
                cancellationToken: cancellationToken);
        return new GrpcOnDemandSandboxActivityWorkerSession(call);
    }

    Metadata? CreateTaskHubHeaders(string taskHub) => this.attachTaskHubMetadata
        ? new Metadata { { "taskhub", taskHub }, }
        : null;

    /// <summary>
    /// gRPC-backed on-demand sandbox activity worker registration session.
    /// </summary>
    sealed class GrpcOnDemandSandboxActivityWorkerSession : IOnDemandSandboxActivityWorkerSession
    {
        readonly AsyncClientStreamingCall<Proto.OnDemandSandboxActivityWorkerMessage, Proto.OnDemandSandboxActivityWorkerSessionResult> call;

        /// <summary>
        /// Initializes a new instance of the <see cref="GrpcOnDemandSandboxActivityWorkerSession"/> class.
        /// </summary>
        /// <param name="call">The active gRPC client-streaming call.</param>
        public GrpcOnDemandSandboxActivityWorkerSession(AsyncClientStreamingCall<Proto.OnDemandSandboxActivityWorkerMessage, Proto.OnDemandSandboxActivityWorkerSessionResult> call)
        {
            this.call = call;
        }

        /// <inheritdoc/>
        public Task WriteMessageAsync(Proto.OnDemandSandboxActivityWorkerMessage message) =>
            this.call.RequestStream.WriteAsync(message);

        /// <inheritdoc/>
        public async Task<Proto.OnDemandSandboxActivityWorkerSessionResult> WaitForCompletionAsync() =>
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
