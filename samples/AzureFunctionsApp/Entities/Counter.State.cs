// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace AzureFunctionsApp.Entities.State;

/// <summary>
/// Example on how to dispatch to a POCO as the entity implementation.
/// </summary>
public class Counter
{
    public int Value { get; set; }

    public int Add(int input) => this.Value += input;

    public int OperationWithContext(int input, TaskEntityContext context)
    {
        // Get access to TaskEntityContext by adding it as a parameter. Can be with or without an input parameter.
        // Order does not matter.
        context.StartOrchestration("SomeOrchestration", "SomeInput");
        return this.Add(input);
    }

    public int Get() => this.Value;

    [Function("Counter1")]
    public static Task RunEntityAsync([EntityTrigger] TaskEntityDispatcher dispatcher)
    {
        // Using the dispatch to a state object will deserialize the state directly to that instance and dispatch to an
        // appropriate method.
        // Can only dispatch to a state object via generic argument.
        return dispatcher.DispatchAsync<Counter>();
    }

    [Function("StartCounter1")]
    public static async Task<HttpResponseData> StartAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData request,
        [DurableClient] DurableTaskClient client)
    {
        Payload? payload = await request.ReadFromJsonAsync<Payload>();
        string id = await client.ScheduleNewOrchestrationInstanceAsync("CounterOrchestration1", payload);
        return client.CreateCheckStatusResponse(request, id);
    }

    [Function("CounterOrchestration1")]
    public static async Task<int> RunOrchestrationAsync(
        [OrchestrationTrigger] TaskOrchestrationContext context, Payload input)
    {
        ILogger logger = context.CreateReplaySafeLogger<Counter>();
        int result = await context.Entities.CallEntityAsync<int>(
            new EntityInstanceId("Counter1", input.Key), "add", input.Add);

        logger.LogInformation("Counter value: {Value}", result);
        return result;
    }

    public record Payload(string Key, int Add);
}
