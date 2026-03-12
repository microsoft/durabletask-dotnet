// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Plugins;
using Microsoft.DurableTask.Plugins.BuiltIn;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Extensions.Plugins.E2ETests;

/// <summary>
/// Shared fixture that sets up a DTS-connected worker and client for E2E tests.
/// Reads the connection string from DTS_CONNECTION_STRING environment variable.
/// </summary>
public sealed class DtsFixture : IAsyncDisposable
{
    IHost? host;

    public DurableTaskClient Client { get; private set; } = null!;

    public MetricsStore MetricsStore { get; } = new();

    public List<string> AuthorizationLog { get; } = new();

    public static string? GetConnectionString() =>
        Environment.GetEnvironmentVariable("DTS_CONNECTION_STRING");

    public async Task StartAsync(
        Action<DurableTaskRegistry> configureTasks,
        Action<IDurableTaskWorkerBuilder>? configureWorker = null)
    {
        string connectionString = GetConnectionString()
            ?? throw new InvalidOperationException(
                "DTS_CONNECTION_STRING environment variable is required. " +
                "Format: Endpoint=https://...;Authentication=DefaultAzure;TaskHub=...");

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddDurableTaskClient(clientBuilder =>
        {
            clientBuilder.UseDurableTaskScheduler(connectionString);
        });

        builder.Services.AddDurableTaskWorker(workerBuilder =>
        {
            workerBuilder.AddTasks(configureTasks);
            workerBuilder.UseDurableTaskScheduler(connectionString);

            // Apply custom plugin configuration.
            configureWorker?.Invoke(workerBuilder);
        });

        this.host = builder.Build();
        await this.host.StartAsync();

        this.Client = this.host.Services.GetRequiredService<DurableTaskClient>();
    }

    public async ValueTask DisposeAsync()
    {
        if (this.host is not null)
        {
            await this.host.StopAsync();
            this.host.Dispose();
        }
    }
}
