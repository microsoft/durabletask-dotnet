// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.Samples.Serverless.MainApp;

internal static class ServerlessSandboxHttpHost
{
    public static async Task RunAsync(
        string endpoint,
        string taskHub,
        string workerProfileId,
        TokenCredential credential)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(new ServerlessSandboxHttpOptions(taskHub, workerProfileId));
        builder.Services.AddDurableTaskClient(clientBuilder =>
        {
            clientBuilder.UseDurableTaskScheduler(options =>
            {
                options.EndpointAddress = endpoint;
                options.TaskHubName = taskHub;
                options.Credential = credential;
            });
        });
        builder.Services.AddDurableTaskSchedulerServerlessActivitiesClient();
        builder.Services.AddControllers();

        string? urls = Environment.GetEnvironmentVariable("DTS_DEMO_HTTP_URLS")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
        if (string.IsNullOrWhiteSpace(urls))
        {
            builder.WebHost.UseUrls("http://localhost:5188");
        }
        else
        {
            builder.WebHost.UseUrls(urls);
        }

        WebApplication app = builder.Build();
        app.MapControllers();
        await app.RunAsync();
    }
}
