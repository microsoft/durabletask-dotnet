// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;
using Azure.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Grpc.Core;
using Grpc.Net.Client;
using Proto = Microsoft.DurableTask.Protobuf.Serverless;

namespace Microsoft.DurableTask.Samples.Serverless.Declarer;

internal static class ServerlessSandboxHttpHost
{
    const string DefaultResourceId = "https://durabletask.io";

    public static async Task RunAsync(
        string[] args,
        string endpoint,
        string taskHub,
        TokenCredential? credential,
        bool allowInsecureCredentials)
    {
        string normalizedEndpoint = NormalizeEndpoint(endpoint);
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton(new ServerlessSandboxHttpOptions(
            normalizedEndpoint,
            taskHub,
            Environment.GetEnvironmentVariable("DTS_WORKER_PROFILE_ID") ?? "default",
            Environment.GetEnvironmentVariable("DTS_RESOURCE_ID") ?? DefaultResourceId,
            allowInsecureCredentials || normalizedEndpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase)));
        if (credential is not null)
        {
            builder.Services.AddSingleton(credential);
        }

        builder.Services.AddSingleton(CreateChannel);
        builder.Services.AddSingleton(provider => new Proto.ServerlessActivities.ServerlessActivitiesClient(
            provider.GetRequiredService<GrpcChannel>()));
        builder.Services.AddControllers().AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

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

    static string NormalizeEndpoint(string endpoint)
    {
        string trimmedEndpoint = endpoint.Trim();
        string normalizedEndpoint = trimmedEndpoint.Contains("://", StringComparison.Ordinal)
            ? trimmedEndpoint
            : $"https://{trimmedEndpoint}";

        if (!Uri.TryCreate(normalizedEndpoint, UriKind.Absolute, out Uri? uri)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException($"DTS_ENDPOINT '{endpoint}' is not a valid absolute URI or host name.");
        }

        return normalizedEndpoint;
    }

    static GrpcChannel CreateChannel(IServiceProvider provider)
    {
        ServerlessSandboxHttpOptions options = provider.GetRequiredService<ServerlessSandboxHttpOptions>();
        TokenCredential? credential = provider.GetService<TokenCredential>();
        TokenRequestContext tokenRequestContext = new([$"{options.ResourceId}/.default"]);

        ChannelCredentials channelCredentials = options.Endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? ChannelCredentials.SecureSsl
            : ChannelCredentials.Insecure;
        CallCredentials callCredentials = CallCredentials.FromInterceptor(async (context, metadata) =>
        {
            metadata.Add("taskhub", options.TaskHub);
            metadata.Add("x-user-agent", "durabletask-dotnet-serverless-sample");

            if (credential is not null)
            {
                AccessToken token = await credential.GetTokenAsync(tokenRequestContext, context.CancellationToken);
                metadata.Add("Authorization", $"Bearer {token.Token}");
            }
        });

        return GrpcChannel.ForAddress(options.Endpoint, new GrpcChannelOptions
        {
            Credentials = ChannelCredentials.Create(channelCredentials, callCredentials),
            UnsafeUseInsecureChannelCallCredentials = options.AllowInsecureCredentials,
        });
    }
}