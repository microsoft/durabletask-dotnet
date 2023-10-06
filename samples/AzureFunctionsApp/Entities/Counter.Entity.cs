// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace AzureFunctionsApp.Entities.Entity;

/// <summary>
/// Example on how to dispatch to an entity which directly implements TaskEntity<TState>. Using TaskEntity<TState> gives
/// the added benefit of being able to use DI. When using TaskEntity<TState>, state is deserialized to the "State"
/// property. No other properties on this type will be serialized/deserialized.
/// </summary>
public class Counter : TaskEntity<int>
{
    readonly ILogger logger;

    public Counter(ILogger<Counter> logger)
    {
        this.logger = logger;
    }

    public int Add(int input)
    {
        this.logger.LogInformation("Adding {Input} to {State}", input, this.State);
        return this.State += input;
    }

    public int OperationWithContext(int input, TaskEntityContext context)
    {
        // Get access to TaskEntityContext by adding it as a parameter. Can be with or without an input parameter.
        // Order does not matter.
        context.StartOrchestration("SomeOrchestration", "SomeInput");

        // When using TaskEntity<TState>, the TaskEntityContext can also be accessed via this.Context.
        this.Context.StartOrchestration("SomeOrchestration", "SomeInput");
        return this.Add(input);
    }

    public int Get() => this.State;

    [Function("Counter2")]
    public Task RunEntityAsync([EntityTrigger] TaskEntityDispatcher dispatcher)
    {
        // Can dispatch to a TaskEntity<TState> by passing a instance.
        return dispatcher.DispatchAsync(this);
    }

    [Function("Counter3")]
    public static Task RunEntityStaticAsync([EntityTrigger] TaskEntityDispatcher dispatcher)
    {
        // Can also dispatch to a TaskEntity<TState> by using a static method.
        return dispatcher.DispatchAsync<Counter>();
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

    protected override int InitializeState()
    {
        // Optional method to override to customize initialization of state for a new instance.
        return base.InitializeState();
    }

    public record Payload(string Key, int Add);
}
