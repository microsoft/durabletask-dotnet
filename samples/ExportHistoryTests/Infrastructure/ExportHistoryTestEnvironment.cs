using Microsoft.Extensions.Configuration;

namespace ExportHistoryTests.Infrastructure;

/// <summary>
/// Helper object that loads the environment configuration required for export history end-to-end tests.
/// </summary>
public sealed class ExportHistoryTestEnvironment
{
    ExportHistoryTestEnvironment()
    {
    }

    /// <summary>
    /// Gets the Durable Task connection string for the Azure Managed service.
    /// </summary>
    public string? SchedulerConnectionString { get; init; }

    /// <summary>
    /// Gets the Azure Storage connection string used for writing exported history blobs.
    /// </summary>
    public string? StorageConnectionString { get; init; }

    /// <summary>
    /// Gets the name of the container that should receive exported payloads.
    /// </summary>
    public string ContainerName { get; init; } = "export-history-e2e";

    /// <summary>
    /// Gets the optional default prefix applied to exported blobs.
    /// </summary>
    public string? DefaultPrefix { get; init; }

    /// <summary>
    /// Gets a value indicating whether every required configuration value is present.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(this.SchedulerConnectionString) &&
        !string.IsNullOrWhiteSpace(this.StorageConnectionString) &&
        !string.IsNullOrWhiteSpace(this.ContainerName);

    /// <summary>
    /// Gets the reason the tests are skipped when configuration is missing.
    /// </summary>
    public string SkipReason =>
        "Export history e2e tests require DURABLE_TASK_CONNECTION_STRING, " +
        "EXPORT_HISTORY_STORAGE_CONNECTION_STRING, and EXPORT_HISTORY_CONTAINER_NAME to be configured.";

    /// <summary>
    /// Loads configuration from appsettings.json and environment variables.
    /// </summary>
    /// <param name="configuration">Optional pre-built configuration.</param>
    /// <returns>The populated <see cref="ExportHistoryTestEnvironment"/>.</returns>
    public static ExportHistoryTestEnvironment Load(IConfiguration? configuration = null)
    {
        IConfiguration config = configuration ?? new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        string? scheduler = config["DURABLE_TASK_CONNECTION_STRING"];
        string? storage = config["EXPORT_HISTORY_STORAGE_CONNECTION_STRING"];
        string container = config["EXPORT_HISTORY_CONTAINER_NAME"] ?? "export-history-e2e";
        string? prefix = config["EXPORT_HISTORY_DEFAULT_PREFIX"];

        return new ExportHistoryTestEnvironment
        {
            SchedulerConnectionString = scheduler,
            StorageConnectionString = storage,
            ContainerName = string.IsNullOrWhiteSpace(container) ? "export-history-e2e" : container,
            DefaultPrefix = string.IsNullOrWhiteSpace(prefix) ? null : prefix.Trim(),
        };
    }
}

