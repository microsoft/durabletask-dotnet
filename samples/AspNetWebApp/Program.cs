using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.DurableTask.Client.AzureManaged;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string endpointAddress = builder.Configuration["DURABLE_TASK_SCHEDULER_ENDPOINT_ADDRESS"]
    ?? throw new InvalidOperationException("Missing required configuration 'DURABLE_TASK_SCHEDULER_ENDPOINT_ADDRESS'");

string taskHubName = builder.Configuration["DURABLE_TASK_SCHEDULER_TASK_HUB_NAME"]
    ?? throw new InvalidOperationException("Missing required configuration 'DURABLE_TASK_SCHEDULER_TASK_HUB_NAME'");

TokenCredential credential = builder.Environment.IsProduction()
    ? new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = builder.Configuration["CONTAINER_APP_UMI_CLIENT_ID"] })
    : new DefaultAzureCredential();

// Add all the generated orchestrations and activities automatically
builder.Services.AddDurableTaskWorker(builder =>
{
    builder.AddTasks(r => r.AddAllGeneratedTasks());
    builder.UseDurableTaskScheduler(endpointAddress, taskHubName, credential);
});

// Register the client, which can be used to start orchestrations
builder.Services.AddDurableTaskClient(builder =>
{
    builder.UseDurableTaskScheduler(endpointAddress, taskHubName, credential);
});

// Configure console logging using the simpler, more compact format
builder.Services.AddLogging(logging =>
{
    logging.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.UseUtcTimestamp = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
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
