using System.Collections.Concurrent;
using Azure.Storage.Blobs;
using ExportHistoryTests.Tasks;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.ExportHistory;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ExportHistoryTests.Infrastructure;

/// <summary>
/// Shared infrastructure that spins up a Durable Task worker and client configured with export history support.
/// </summary>
public sealed class ExportHistoryTestFixture : IAsyncLifetime
{
    readonly ConcurrentDictionary<string, BlobContainerClient> containerCache = new(StringComparer.OrdinalIgnoreCase);
    IHost? host;

    public ExportHistoryTestEnvironment Environment { get; private set; } = ExportHistoryTestEnvironment.Load();

    public DurableTaskClient? DurableTaskClient { get; private set; }

    public ExportHistoryClient? ExportHistoryClient { get; private set; }

    public BlobServiceClient? BlobServiceClient { get; private set; }

    public ILogger<ExportHistoryTestFixture>? Logger { get; private set; }

    public BlobContainerClient? DefaultContainerClient { get; private set; }

    public bool IsConfigured => this.Environment.IsConfigured;

    public async Task InitializeAsync()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        this.Environment = ExportHistoryTestEnvironment.Load(configuration);

        if (!this.Environment.IsConfigured)
        {
            return;
        }

        HostBuilder builder = new();
        builder.ConfigureAppConfiguration(configBuilder =>
        {
            configBuilder.AddConfiguration(configuration);
        });

        builder.ConfigureServices((context, services) =>
        {
            services.AddLogging(logging =>
            {
                logging.AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
                });
            });

            services.AddDurableTaskWorker(workerBuilder =>
            {
                workerBuilder.UseDurableTaskScheduler(this.Environment.SchedulerConnectionString!);
                workerBuilder.UseExportHistory();
                workerBuilder.AddTasks(registration =>
                {
                    registration.AddOrchestrator<ExportHistoryTestOrchestrator>();
                    registration.AddOrchestrator<ExportHistoryTestChildOrchestrator>();
                    registration.AddActivity<ExportHistoryTestActivity>();
                });
            });

            services.AddDurableTaskClient(clientBuilder =>
            {
                clientBuilder.UseDurableTaskScheduler(this.Environment.SchedulerConnectionString!);
                clientBuilder.UseExportHistory(options =>
                {
                    options.ConnectionString = this.Environment.StorageConnectionString!;
                    options.ContainerName = this.Environment.ContainerName;
                    if (!string.IsNullOrWhiteSpace(this.Environment.DefaultPrefix))
                    {
                        options.Prefix = this.Environment.DefaultPrefix;
                    }
                });
            });
        });

        this.host = builder.Build();
        await this.host.StartAsync();

        IServiceProvider services = this.host.Services;
        this.DurableTaskClient = services.GetRequiredService<DurableTaskClient>();
        this.ExportHistoryClient = services.GetRequiredService<ExportHistoryClient>();
        this.Logger = services.GetRequiredService<ILogger<ExportHistoryTestFixture>>();

        this.BlobServiceClient = new BlobServiceClient(this.Environment.StorageConnectionString);
        this.DefaultContainerClient = this.BlobServiceClient.GetBlobContainerClient(this.Environment.ContainerName);
        await this.DefaultContainerClient.CreateIfNotExistsAsync();

        this.containerCache[this.Environment.ContainerName] = this.DefaultContainerClient;
    }

    public async Task DisposeAsync()
    {
        if (this.host != null)
        {
            await this.host.StopAsync();
            this.host.Dispose();
        }

        this.containerCache.Clear();
        this.BlobServiceClient = null;
        this.DefaultContainerClient = null;
        this.DurableTaskClient = null;
        this.ExportHistoryClient = null;
    }

    public void SkipIfNotConfigured()
    {
        if (!this.IsConfigured)
        {
            throw new InvalidOperationException(
                $"Test skipped: {this.Environment.SkipReason}. " +
                "Please configure the required environment variables: " +
                "DURABLE_TASK_CONNECTION_STRING, EXPORT_HISTORY_STORAGE_CONNECTION_STRING, " +
                "EXPORT_HISTORY_CONTAINER_NAME");
        }
    }

    public async Task<BlobContainerClient> GetContainerClientAsync(string containerName, CancellationToken cancellationToken = default)
    {
        this.SkipIfNotConfigured();
        if (this.BlobServiceClient == null)
        {
            throw new InvalidOperationException("BlobServiceClient is not initialized.");
        }

        if (this.containerCache.TryGetValue(containerName, out BlobContainerClient? client))
        {
            return client;
        }

        BlobContainerClient newClient = this.BlobServiceClient.GetBlobContainerClient(containerName);
        await newClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        this.containerCache[containerName] = newClient;
        return newClient;
    }
}

