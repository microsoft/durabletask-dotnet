// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Extension methods for configuring Durable Task clients to use export history.
/// </summary>
public static class DurableTaskClientBuilderExtensions
{
    /// <summary>
    /// Enables export history support for the client builder with Azure Storage configuration.
    /// </summary>
    /// <param name="builder">The client builder to add export history support to.</param>
    /// <param name="configure">Callback to configure Azure Storage options. Must not be null.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskClientBuilder UseExportHistory(
        this IDurableTaskClientBuilder builder,
        Action<ExportHistoryStorageOptions> configure)
    {
        Check.NotNull(builder, nameof(builder));
        Check.NotNull(configure, nameof(configure));

        IServiceCollection services = builder.Services;

        // Register and validate options
        services.AddOptions<ExportHistoryStorageOptions>()
                .Configure(configure)
                .Validate(
                    o =>
                    !string.IsNullOrEmpty(o.ConnectionString) &&
                    !string.IsNullOrEmpty(o.ContainerName),
                    $"{nameof(ExportHistoryStorageOptions)} must specify both {nameof(ExportHistoryStorageOptions.ConnectionString)} and {nameof(ExportHistoryStorageOptions.ContainerName)}.");

        // Register ExportHistoryClient using validated options
        services.AddSingleton<ExportHistoryClient>(sp =>
        {
            DurableTaskClient durableTaskClient = sp.GetRequiredService<DurableTaskClient>();
            ILogger<DefaultExportHistoryClient> logger = sp.GetRequiredService<ILogger<DefaultExportHistoryClient>>();
            ExportHistoryStorageOptions options = sp.GetRequiredService<IOptions<ExportHistoryStorageOptions>>().Value;

            return new DefaultExportHistoryClient(durableTaskClient, logger, options);
        });

        return builder;
    }
}
