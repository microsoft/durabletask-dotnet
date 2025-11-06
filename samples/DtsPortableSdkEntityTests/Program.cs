// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using DtsPortableSdkEntityTests;
using DurableTask.Core.Entities;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string connectionString = builder.Configuration["DTS_CONNECTION_STRING"] ??
    // By default, use the connection string for the local development emulator
    "Endpoint=http://localhost:8080;TaskHub=default;Authentication=None";

// Add all the generated orchestrations and activities automatically
builder.Services.AddDurableTaskWorker(builder =>
{
    builder.AddTasks(r =>
    {
        // TODO consider using source generator

        // register all orchestrations and activities used in the tests
        HashSet<Type> registeredTestTypes = [];
        foreach(var test in All.GetAllTests())
        {
            if (!registeredTestTypes.Contains(test.GetType()))
            {
                test.Register(r, builder.Services);
                registeredTestTypes.Add(test.GetType());
            }
        }

        // register all entities
        BatchEntity.Register(r);
        Counter.Register(r);
        FaultyEntity.Register(r);
        Launcher.Register(r);
        Relay.Register(r);
        SchedulerEntity.Register(r);
        SelfSchedulingEntity.Register(r);
        StringStore.Register(r);
        StringStore2.Register(r);
        StringStore3.Register(r);
    });

    builder.UseDurableTaskScheduler(connectionString);
});

// Register the client, which can be used to start orchestrations
builder.Services.AddDurableTaskClient(builder =>
{
    builder.UseDurableTaskScheduler(connectionString);
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
