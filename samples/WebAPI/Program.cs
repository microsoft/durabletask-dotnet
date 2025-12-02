// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;
using DurableTask.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Testing.Sidecar;
using Microsoft.DurableTask.Testing.Sidecar.Grpc;
using Microsoft.DurableTask.Worker;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// The REST API will listen on port 8080 (HTTP/1.1) and the gRPC sidecar will listen on port 8081 (HTTP/2)
int restPort = 8080;
int grpcPort = 8081;

// Configure Kestrel with two endpoints:
// - Port 8080 for REST API (HTTP/1.1)
// - Port 8081 for gRPC sidecar (HTTP/2)
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(restPort, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1;
    });
    options.ListenAnyIP(grpcPort, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
    });
});

// Create the in-memory orchestration service that will store all orchestration state.
// This eliminates the need for an external gRPC sidecar.
InMemoryOrchestrationService orchestrationService = new();

// Register the in-memory orchestration service for the gRPC sidecar
builder.Services.AddSingleton<IOrchestrationService>(orchestrationService);
builder.Services.AddSingleton<IOrchestrationServiceClient>(orchestrationService);
builder.Services.AddSingleton<TaskHubGrpcServer>();
builder.Services.AddGrpc();

// Create the gRPC channel that will connect to the embedded sidecar on the gRPC port
GrpcChannel grpcChannel = GrpcChannel.ForAddress($"http://localhost:{grpcPort}");

// Add all the generated tasks and configure worker to use the embedded gRPC sidecar
builder.Services.AddDurableTaskWorker(builder =>
{
    builder.AddTasks(r => r.AddAllGeneratedTasks());
    builder.UseGrpc(grpcChannel);
});

// Configure the durable task client to use the embedded gRPC sidecar
builder.Services.AddDurableTaskClient(b =>
{
    b.UseGrpc(grpcChannel);
    b.RegisterDirectly();
});

// Configure the HTTP request pipeline.
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

WebApplication app = builder.Build();

// Map the gRPC sidecar service endpoint
app.MapGrpcService<TaskHubGrpcServer>();

app.MapControllers();
app.Run();
