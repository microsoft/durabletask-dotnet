// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.ExportHistory;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string connectionString = builder.Configuration.GetValue<string>("DURABLE_TASK_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Missing required configuration 'DURABLE_TASK_CONNECTION_STRING'");

string storageConnectionString = builder.Configuration.GetValue<string>("EXPORT_HISTORY_STORAGE_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Missing required configuration 'EXPORT_HISTORY_STORAGE_CONNECTION_STRING'");

string containerName = builder.Configuration.GetValue<string>("EXPORT_HISTORY_CONTAINER_NAME")
    ?? throw new InvalidOperationException("Missing required configuration 'EXPORT_HISTORY_CONTAINER_NAME'");

builder.Services.AddSingleton<ILogger>(sp => sp.GetRequiredService<ILoggerFactory>().CreateLogger<Program>());
builder.Services.AddLogging();

// Add Durable Task worker with export history support
builder.Services.AddDurableTaskWorker(builder =>
{
    builder.UseDurableTaskScheduler(connectionString);
    builder.UseExportHistory();
});

// Register the client with export history support
builder.Services.AddDurableTaskClient(clientBuilder =>
{
    clientBuilder.UseDurableTaskScheduler(connectionString);
    clientBuilder.UseExportHistory(options =>
    {
        options.ConnectionString = storageConnectionString;
        options.ContainerName = containerName;
        options.Prefix = builder.Configuration.GetValue<string>("EXPORT_HISTORY_PREFIX");
    });
});

// Configure the HTTP request pipeline
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// The actual listen URL can be configured in environment variables named "ASPNETCORE_URLS" or "ASPNETCORE_URLS_HTTPS"
WebApplication app = builder.Build();
app.MapControllers();
app.Run();

