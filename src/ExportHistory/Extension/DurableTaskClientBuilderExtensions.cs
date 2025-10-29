// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Extension methods for configuring Durable Task clients to use scheduled tasks.
/// </summary>
public static class DurableTaskClientBuilderExtensions
{
    /// <summary>
    /// Enables export history support for the client builder.
    /// </summary>
    /// <param name="builder">The client builder to add export history support to.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskClientBuilder UseExportHistory(this IDurableTaskClientBuilder builder)
    {
        builder.Services.AddSingleton<ExportHistoryClient, DefaultExportHistoryClient>();
        return builder;
    }
}
