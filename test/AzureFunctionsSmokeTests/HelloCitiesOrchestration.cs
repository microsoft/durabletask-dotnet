// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace AzureFunctionsSmokeTests;

/// <summary>
/// Smoke test orchestration functions for Azure Functions with Durable Task.
/// </summary>
public static class HelloCitiesOrchestration
{
    [Function(nameof(HelloCitiesOrchestration))]
    public static async Task<List<string>> RunOrchestrator(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        ILogger logger = context.CreateReplaySafeLogger(nameof(HelloCitiesOrchestration));
        logger.LogInformation("Starting HelloCities orchestration.");

        List<string> outputs = new List<string>();

        // Call activities in sequence
        outputs.Add(await context.CallActivityAsync<string>(nameof(SayHello), "Tokyo"));
        outputs.Add(await context.CallActivityAsync<string>(nameof(SayHello), "Seattle"));
        outputs.Add(await context.CallActivityAsync<string>(nameof(SayHello), "London"));

        logger.LogInformation("HelloCities orchestration completed.");

        // returns ["Hello Tokyo!", "Hello Seattle!", "Hello London!"]
        return outputs;
    }

    [Function(nameof(SayHello))]
    public static string SayHello([ActivityTrigger] string name, FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger(nameof(SayHello));
        logger.LogInformation($"Saying hello to {name}.");
        return $"Hello {name}!";
    }

    [Function("HelloCitiesOrchestration_HttpStart")]
    public static async Task<HttpResponseData> HttpStart(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger("HelloCitiesOrchestration_HttpStart");

        // Function input comes from the request content.
        string instanceId = await client
            .ScheduleNewOrchestrationInstanceAsync(nameof(HelloCitiesOrchestration));

        logger.LogInformation($"Started orchestration with ID = '{instanceId}'.");

        // Returns an HTTP 202 response with an instance management payload.
        return client.CreateCheckStatusResponse(req, instanceId);
    }
}
