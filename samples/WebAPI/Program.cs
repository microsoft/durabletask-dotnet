// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;
using Microsoft.DurableTask;
using Dapr.DurableTask.Client;
using Dapr.DurableTask.Worker;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add all the generated tasks
builder.Services.AddDurableTaskWorker(builder =>
{
    builder.AddTasks(r => r.AddAllGeneratedTasks());
    builder.UseGrpc();
});

builder.Services.AddDurableTaskClient(b => b.UseGrpc());

// Configure the HTTP request pipeline.
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

WebApplication app = builder.Build();
app.MapControllers();
app.Run("http://0.0.0.0:8080");
