// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Dapr.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace AzureFunctionsApp;

/// <summary>
/// A simple greeting orchestration to demonstrate passing custom input and output data.
/// </summary>
public static class Greeting
{
    [Function(nameof(Greeting))]
    public static async Task<HttpResponseData> StartGreeting(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger(nameof(StartGreeting));

        Input? payload = await req.ReadFromJsonAsync<Input>();
        string instanceId = await client
            .ScheduleNewOrchestrationInstanceAsync(nameof(GreetingOrchestration), payload);
        logger.LogInformation("Created new orchestration with instance ID = {instanceId}", instanceId);

        return client.CreateCheckStatusResponse(req, instanceId);
    }

    [Function(nameof(GreetingOrchestration))]
    public static async Task<Input> GreetingOrchestration(
        [OrchestrationTrigger] TaskOrchestrationContext context, Input input)
    {
        Input result = await context.CallActivityAsync<Input>(nameof(GreetingActivity), input);
        return result;
    }

    [Function(nameof(GreetingActivity))]
    public static Input GreetingActivity([ActivityTrigger] Input input, FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger(nameof(GreetingActivity));
        logger.LogInformation("Saying hello to {input}", input);
        return input;
    }


    public record Input(string Name, int Age)
    {
        public string? CustomMessage { get; init; }
    }
}
