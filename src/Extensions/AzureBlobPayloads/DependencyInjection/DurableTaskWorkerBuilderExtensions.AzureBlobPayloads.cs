// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Extension methods to enable externalized payloads using Azure Blob Storage for Durable Task Worker.
/// </summary>
public static class DurableTaskWorkerBuilderExtensionsAzureBlobPayloads
{
    /// <summary>
    /// Enables externalized payload storage using Azure Blob Storage for the specified worker builder.
    /// </summary>
    public static IDurableTaskWorkerBuilder UseExternalizedPayloads(
        this IDurableTaskWorkerBuilder builder,
        Action<LargePayloadStorageOptions> configure)
    {
        Check.NotNull(builder);
        Check.NotNull(configure);

        builder.Services.Configure(builder.Name, configure);
        builder.Services.AddSingleton<IPayloadStore>(sp =>
        {
            LargePayloadStorageOptions opts = sp.GetRequiredService<IOptionsMonitor<LargePayloadStorageOptions>>().Get(builder.Name);
            return new BlobPayloadStore(opts);
        });

        // builder.Services
        //     .AddOptions<DurableTaskWorkerOptions>(builder.Name)
        //     .PostConfigure<IPayloadStore, IOptionsMonitor<LargePayloadStorageOptions>>((opt, store, monitor) =>
        //     {
        //         LargePayloadStorageOptions opts = monitor.Get(builder.Name);
        //         DataConverter inner = opt.DataConverter ?? Converters.JsonDataConverter.Default;
        //         opt.DataConverter = new LargePayloadDataConverter(inner, store, opts);
        //     });

        return builder;
    }
}


