// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Proto = Microsoft.DurableTask.Protobuf.Serverless;

namespace Microsoft.DurableTask.Worker.AzureManaged.Serverless;

/// <summary>
/// Client abstraction for the serverless activities gRPC service.
/// </summary>
interface IServerlessActivitiesClient
{
    /// <summary>
    /// Declares remote activities to DTS.
    /// </summary>
    /// <param name="declaration">The declaration message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The declaration result.</returns>
    Task<Proto.RemoteActivityDeclarationResult> DeclareRemoteActivitiesAsync(
        Proto.RemoteActivityDeclaration declaration,
        CancellationToken cancellationToken);

    /// <summary>
    /// Opens a remote activity worker registration session.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The worker registration session.</returns>
    IRemoteActivityWorkerSession OpenRemoteActivityWorkerSession(CancellationToken cancellationToken);
}

/// <summary>
/// Client-streaming session used by a remote activity worker registration.
/// </summary>
interface IRemoteActivityWorkerSession : IAsyncDisposable
{
    /// <summary>
    /// Writes a worker registration message to the stream.
    /// </summary>
    /// <param name="message">The message to write.</param>
    /// <returns>A task that completes when the message is written.</returns>
    Task WriteMessageAsync(Proto.RemoteActivityWorkerMessage message);

    /// <summary>
    /// Completes the request stream.
    /// </summary>
    /// <returns>A task that completes when the stream is completed.</returns>
    Task CompleteAsync();
}

/// <summary>
/// gRPC-backed implementation of <see cref="IServerlessActivitiesClient"/>.
/// </summary>
sealed class ServerlessActivitiesClientAdapter : IServerlessActivitiesClient
{
    readonly Proto.ServerlessActivities.ServerlessActivitiesClient client;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerlessActivitiesClientAdapter"/> class.
    /// </summary>
    /// <param name="client">The generated serverless activities gRPC client.</param>
    public ServerlessActivitiesClientAdapter(Proto.ServerlessActivities.ServerlessActivitiesClient client)
    {
        this.client = Check.NotNull(client);
    }

    /// <inheritdoc/>
    public async Task<Proto.RemoteActivityDeclarationResult> DeclareRemoteActivitiesAsync(
        Proto.RemoteActivityDeclaration declaration,
        CancellationToken cancellationToken)
    {
        return await this.client.DeclareRemoteActivitiesAsync(declaration, cancellationToken: cancellationToken)
            .ResponseAsync.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public IRemoteActivityWorkerSession OpenRemoteActivityWorkerSession(CancellationToken cancellationToken)
    {
        AsyncClientStreamingCall<Proto.RemoteActivityWorkerMessage, Proto.RemoteActivityWorkerSessionResult> call =
            this.client.ConnectRemoteActivityWorker(cancellationToken: cancellationToken);
        return new GrpcRemoteActivityWorkerSession(call);
    }

    /// <summary>
    /// gRPC-backed remote activity worker registration session.
    /// </summary>
    sealed class GrpcRemoteActivityWorkerSession : IRemoteActivityWorkerSession
    {
        readonly AsyncClientStreamingCall<Proto.RemoteActivityWorkerMessage, Proto.RemoteActivityWorkerSessionResult> call;

        /// <summary>
        /// Initializes a new instance of the <see cref="GrpcRemoteActivityWorkerSession"/> class.
        /// </summary>
        /// <param name="call">The active gRPC client-streaming call.</param>
        public GrpcRemoteActivityWorkerSession(AsyncClientStreamingCall<Proto.RemoteActivityWorkerMessage, Proto.RemoteActivityWorkerSessionResult> call)
        {
            this.call = call;
        }

        /// <inheritdoc/>
        public Task WriteMessageAsync(Proto.RemoteActivityWorkerMessage message) =>
            this.call.RequestStream.WriteAsync(message);

        /// <inheritdoc/>
        public Task CompleteAsync() => this.call.RequestStream.CompleteAsync();

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            this.call.Dispose();
            return default;
        }
    }
}
