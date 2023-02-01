// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace AzureFunctionsApp;

/// <summary>
/// An example of performing the fibonacci sequence in Durable Functions. While this is both a (naive) recursive
/// implementation of fibonacci and also not the best use of Durable, it does a good job at highlighting some patterns
/// that can be used in durable. Particularly:
/// 1. Sub orchestrations
/// 2. Orchestration flexibility - can be both a top level AND a sub orchestration
/// 3. Recursion can be performed with orchestrations!
/// 4. Control flow you are used to from regular C# programming works here as well! Particularly branching.
/// 5. Concurrency can be controlled like any other C# Task.
/// </summary>
static class Fib
{
    [Function(nameof(Fib))]
    public static async Task<HttpResponseData> Start(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger(nameof(Fib));

        int? payload = await req.ReadFromJsonAsync<int>();
        string instanceId = await client
            .ScheduleNewOrchestrationInstanceAsync(nameof(FibOrchestration), payload);
        logger.LogInformation("Created new orchestration with instance ID = {instanceId}", instanceId);

        return client.CreateCheckStatusResponse(req, instanceId);
    }

    [Function(nameof(FibOrchestration))]
    public static async Task<int> FibOrchestration([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        int input = context.GetInput<int>()!;
        switch (input)
        {
            case 0 or 1:
                // The activity call is here just to demonstrate how an orchestration can mix activity and
                // sub-orchestration calls
                return await context.CallActivityAsync<int>(nameof(FibActivity), input);
            default:
                // Fan out / fan in (concurrency) is done by simply invoking multiple sub orchestrations (or activities,
                // or a mix of the two) at once (without yielding), then unwrapping them both. You can also use C#
                // helpers like Task.WhenAll.
                Task<int> left = context.CallSubOrchestratorAsync<int>(nameof(FibOrchestration), input - 1);
                Task<int> right = context.CallSubOrchestratorAsync<int>(nameof(FibOrchestration), input - 2);
                return (await left) + (await right);
        }
    }

    [Function(nameof(FibActivity))]
    public static int FibActivity([ActivityTrigger] int input, FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger(nameof(FibActivity));
        logger.LogInformation("Fib leaf of {input}", input);
        return input;
    }
}
