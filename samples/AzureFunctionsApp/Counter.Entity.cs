// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace AzureFunctionsApp.Entity;

/// <summary>
/// Example on how to dispatch to an entity which directly implements TaskEntity<TState>.
/// </summary>
public class Counter : TaskEntity<int>
{
    public int Add(int input) => this.State += input;

    public int Get() => this.State;

    [Function("Counter2")]
    public Task RunEntityAsync([EntityTrigger] TaskEntityDispatcher dispatcher)
    {
        return dispatcher.DispatchAsync(this);
    }

    [Function("StartCounter2")]
    public static async Task<HttpResponseData> StartAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData request,
        [DurableClient] DurableTaskClient client)
    {
        Payload? payload = await request.ReadFromJsonAsync<Payload>();
        string id = await client.ScheduleNewOrchestrationInstanceAsync("CounterOrchestration2", payload);
        return client.CreateCheckStatusResponse(request, id);
    }

    [Function("CounterOrchestration2")]
    public static async Task<int> RunOrchestrationAsync(
        [OrchestrationTrigger] TaskOrchestrationContext context, Payload input)
    {
        ILogger logger = context.CreateReplaySafeLogger<Counter>();
        int result = await context.Entities.CallEntityAsync<int>(
            new EntityInstanceId("Counter2", input.Key), "add", input.Add);

        logger.LogInformation("Counter value: {Value}", result);
        return result;
    }

    public record Payload(string Key, int Add);
}
