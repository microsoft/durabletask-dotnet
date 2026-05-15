// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker.AzureManaged.Serverless;

/// <summary>
/// Hosts a private HTTP listener that wakes or probes a serverless worker container.
/// </summary>
public sealed partial class ServerlessWakeupServer : IHostedService, IAsyncDisposable
{
    readonly ServerlessOptions options;
    readonly ILogger<ServerlessWakeupServer> logger;
    WebApplication? app;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerlessWakeupServer"/> class.
    /// </summary>
    /// <param name="options">The serverless options.</param>
    /// <param name="logger">The logger.</param>
    public ServerlessWakeupServer(ServerlessOptions options, ILogger<ServerlessWakeupServer> logger)
    {
        this.options = Check.NotNull(options);
        this.logger = Check.NotNull(logger);
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (this.options.Mode != ServerlessMode.ServerlessInclude || this.app is not null)
        {
            return;
        }

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(this.options.WakeupPort));
        builder.Logging.ClearProviders();

        WebApplication localApp = builder.Build();
        localApp.MapPost("/", static () => Results.Ok());
        localApp.MapPost("/wakeup", static () => Results.Ok());
        localApp.MapGet("/health", static () => Results.Ok());

        try
        {
            await localApp.StartAsync(cancellationToken).ConfigureAwait(false);
            this.app = localApp;
        }
        catch
        {
            await localApp.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        Log.Started(this.logger, this.options.WakeupPort);
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        WebApplication? localApp = this.app;
        this.app = null;

        if (localApp is null)
        {
            return;
        }

        try
        {
            await localApp.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await localApp.DisposeAsync().ConfigureAwait(false);
            Log.Stopped(this.logger, this.options.WakeupPort);
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => new(this.StopAsync(CancellationToken.None));

    static partial class Log
    {
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Information,
            Message = "Serverless wakeup server listening on port {Port}")]
        public static partial void Started(ILogger logger, int port);

        [LoggerMessage(
            EventId = 2,
            Level = LogLevel.Information,
            Message = "Serverless wakeup server stopped on port {Port}")]
        public static partial void Stopped(ILogger logger, int port);
    }
}
