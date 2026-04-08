// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace DepValidationApp;

/// <summary>
/// A simple function chaining orchestration used to smoke-test that the
/// locally-packed DurableTask SDK NuGet packages work correctly at runtime
/// with the Azure Functions Worker extension.
/// </summary>
public static class HelloCitiesOrchestration
{
    /// <summary>
    /// Returns the versions of the loaded DurableTask SDK assemblies so
    /// smoke tests can verify the correct package versions are being used.
    /// </summary>
    [Function("SdkVersionCheck")]
    public static HttpResponseData SdkVersionCheck(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
    {
        string[] sdkAssemblyPrefixes = new[]
        {
            "Microsoft.DurableTask.Abstractions",
            "Microsoft.DurableTask.Client",
            "Microsoft.DurableTask.Client.Grpc",
            "Microsoft.DurableTask.Worker",
            "Microsoft.DurableTask.Worker.Grpc",
            "Microsoft.DurableTask.Grpc",
        };

        SortedDictionary<string, string> loadedVersions = new();
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            AssemblyName name = asm.GetName();
            foreach (string prefix in sdkAssemblyPrefixes)
            {
                if (string.Equals(name.Name, prefix, StringComparison.OrdinalIgnoreCase))
                {
                    string? infoVersion = asm
                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                        ?.InformationalVersion;

                    // Strip source-link commit hash suffix (e.g. "1.23.3+abc123")
                    if (infoVersion != null && infoVersion.Contains('+'))
                    {
                        infoVersion = infoVersion[..infoVersion.IndexOf('+')];
                    }

                    loadedVersions[name.Name!] = infoVersion ?? name.Version?.ToString() ?? "unknown";
                }
            }
        }

        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        response.WriteString(JsonSerializer.Serialize(loadedVersions));
        return response;
    }

    [Function(nameof(HelloCitiesOrchestration))]
    public static async Task<string> RunOrchestrator(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        string result = "";
        result += await context.CallActivityAsync<string>(nameof(SayHello), "Tokyo") + " ";
        result += await context.CallActivityAsync<string>(nameof(SayHello), "London") + " ";
        result += await context.CallActivityAsync<string>(nameof(SayHello), "Seattle");
        return result;
    }

    [Function(nameof(SayHello))]
    public static string SayHello([ActivityTrigger] string cityName, FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger(nameof(SayHello));
        logger.LogInformation("Saying hello to {CityName}!", cityName);
        return $"Hello, {cityName}!";
    }

    [Function("HelloCitiesOrchestration_HttpStart")]
    public static async Task<HttpResponseData> HttpStart(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger("HelloCitiesOrchestration_HttpStart");
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(nameof(HelloCitiesOrchestration));
        logger.LogInformation("Started orchestration with ID = '{InstanceId}'.", instanceId);
        return client.CreateCheckStatusResponse(req, instanceId);
    }
}
