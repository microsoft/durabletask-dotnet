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
    /// Declares serverless activities to DTS.
    /// </summary>
    /// <param name="declaration">The declaration message.</param>
    /// <param name="taskHub">The task hub that owns the declaration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The declaration result.</returns>
    Task<Proto.ServerlessActivityDeclarationResult> DeclareServerlessActivitiesAsync(
        Proto.ServerlessActivityDeclaration declaration,
        string taskHub,
        CancellationToken cancellationToken);

    /// <summary>
    /// Opens a serverless activity worker registration session.
    /// </summary>
    /// <param name="taskHub">The task hub that owns the worker session.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The worker registration session.</returns>
    IServerlessActivityWorkerSession OpenServerlessActivityWorkerSession(string taskHub, CancellationToken cancellationToken);
}

/// <summary>
/// Client-streaming session used by a serverless activity worker registration.
/// </summary>
interface IServerlessActivityWorkerSession : IAsyncDisposable
{
    /// <summary>
    /// Writes a worker registration message to the stream.
    /// </summary>
    /// <param name="message">The message to write.</param>
    /// <returns>A task that completes when the message is written.</returns>
    Task WriteMessageAsync(Proto.ServerlessActivityWorkerMessage message);

    /// <summary>
    /// Waits for the server to complete the worker registration session.
    /// </summary>
    /// <returns>The worker session result.</returns>
    Task<Proto.ServerlessActivityWorkerSessionResult> WaitForCompletionAsync();

    /// <summary>
    /// Completes the request stream and waits for the server response.
    /// </summary>
    /// <returns>A task that completes when the server response is observed.</returns>
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
    public async Task<Proto.ServerlessActivityDeclarationResult> DeclareServerlessActivitiesAsync(
        Proto.ServerlessActivityDeclaration declaration,
        string taskHub,
        CancellationToken cancellationToken)
    {
        return await this.client.DeclareServerlessActivitiesAsync(
                declaration,
                headers: CreateTaskHubHeaders(taskHub),
                cancellationToken: cancellationToken)
            .ResponseAsync.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public IServerlessActivityWorkerSession OpenServerlessActivityWorkerSession(string taskHub, CancellationToken cancellationToken)
    {
        AsyncClientStreamingCall<Proto.ServerlessActivityWorkerMessage, Proto.ServerlessActivityWorkerSessionResult> call =
            this.client.ConnectServerlessActivityWorker(headers: CreateTaskHubHeaders(taskHub), cancellationToken: cancellationToken);
        return new GrpcServerlessActivityWorkerSession(call);
    }

    static Metadata CreateTaskHubHeaders(string taskHub) => new() { { "taskhub", taskHub } };

    /// <summary>
    /// gRPC-backed serverless activity worker registration session.
    /// </summary>
    sealed class GrpcServerlessActivityWorkerSession : IServerlessActivityWorkerSession
    {
        readonly AsyncClientStreamingCall<Proto.ServerlessActivityWorkerMessage, Proto.ServerlessActivityWorkerSessionResult> call;

        /// <summary>
        /// Initializes a new instance of the <see cref="GrpcServerlessActivityWorkerSession"/> class.
        /// </summary>
        /// <param name="call">The active gRPC client-streaming call.</param>
        public GrpcServerlessActivityWorkerSession(AsyncClientStreamingCall<Proto.ServerlessActivityWorkerMessage, Proto.ServerlessActivityWorkerSessionResult> call)
        {
            this.call = call;
        }

        /// <inheritdoc/>
        public Task WriteMessageAsync(Proto.ServerlessActivityWorkerMessage message) =>
            this.call.RequestStream.WriteAsync(message);

        /// <inheritdoc/>
        public async Task<Proto.ServerlessActivityWorkerSessionResult> WaitForCompletionAsync() =>
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
