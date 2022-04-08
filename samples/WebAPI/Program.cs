// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;
using Microsoft.DurableTask;

var builder = WebApplication.CreateBuilder(args);

// Add all the generated tasks
builder.Services.AddDurableTask(taskRegistry => taskRegistry.AddAllGeneratedTasks());

// Configure the HTTP request pipeline.
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

WebApplication app = builder.Build();
app.MapControllers();
app.Run("http://0.0.0.0:8080");
