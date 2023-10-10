// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace AzureFunctionsApp.Entities;

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
        context.ScheduleNewOrchestration("SomeOrchestration", "SomeInput");

        // When using TaskEntity<TState>, the TaskEntityContext can also be accessed via this.Context.
        this.Context.ScheduleNewOrchestration("SomeOrchestration", "SomeInput");
        return this.Add(input);
    }

    public int Get() => this.State;

    [Function("Counter")]
    public Task RunEntityAsync([EntityTrigger] TaskEntityDispatcher dispatcher)
    {
        // Can dispatch to a TaskEntity<TState> by passing a instance.
        return dispatcher.DispatchAsync(this);
    }

    [Function("Counter_Alt")]
    public static Task RunEntityStaticAsync([EntityTrigger] TaskEntityDispatcher dispatcher)
    {
        // Can also dispatch to a TaskEntity<TState> by using a static method.
        return dispatcher.DispatchAsync<Counter>();
    }

    protected override int InitializeState(TaskEntityOperation operation)
    {
        // Optional method to override to customize initialization of state for a new instance.
        return base.InitializeState(operation);
    }
}

/// <summary>
/// Example on how to dispatch to a POCO as the entity implementation. When using POCO, the entire object is serialized
/// and deserialized.
/// </summary>
/// <remarks>
/// Note there is a structural difference between <see cref="Counter"/> and <see cref="StateCounter"/>. In the former,
/// the state is declared as <see cref="int"/>. In the later, state is the type itself (<see cref="StateCounter"/>).
/// This means they have a different state serialization structure. The former is just "int", while the later is
/// "{ \"Value\": int }".
/// </remarks>
public class StateCounter
{
    public int Value { get; set; }

    public int Add(int input) => this.Value += input;

    public int OperationWithContext(int input, TaskEntityContext context)
    {
        // Get access to TaskEntityContext by adding it as a parameter. Can be with or without an input parameter.
        // Order does not matter.
        context.ScheduleNewOrchestration("SomeOrchestration", "SomeInput");
        return this.Add(input);
    }

    public int Get() => this.Value;

    [Function("StateCounter")]
    public static Task RunEntityAsync([EntityTrigger] TaskEntityDispatcher dispatcher)
    {
        // Using the dispatch to a state object will deserialize the state directly to that instance and dispatch to an
        // appropriate method.
        // Can only dispatch to a state object via generic argument.
        return dispatcher.DispatchAsync<StateCounter>();
    }
}

public static class CounterHelpers
{
    /// <summary>
    /// Usage:
    /// Add to <see cref="Counter"/>:
    /// POST /api/counter/{id}?value={value-to-add}
    /// POST /api/counter/{id}?value={value-to-add}&mode=0
    /// POST /api/counter/{id}?value={value-to-add}&mode=entity
    /// 
    /// Add to <see cref="StateCounter"/>
    /// POST /api/counter/{id}?value={value-to-add}&mode=1
    /// POST /api/counter/{id}?value={value-to-add}&mode=state
    /// </summary>
    /// <param name="request"></param>
    /// <param name="client"></param>
    /// <param name="id"></param>
    /// <returns></returns>
    [Function("StartCounter")]
    public static async Task<HttpResponseData> StartAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "counter/{id}")] HttpRequestData request,
        [DurableClient] DurableTaskClient client,
        string id)
    {
        _ = int.TryParse(request.Query["value"], out int value);

        string name = "counter";

        // switch to StateCounter if ?mode=1 or ?mode=state is supplied.
        string? mode = request.Query["mode"];
        if (int.TryParse(mode, out int m) && m == 1)
        {
            name = "state";
        }
        else if (mode == "state")
        {
            name = "statecounter";
        }

        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
            "CounterOrchestration", new Payload(new EntityInstanceId(name, id), value));
        return client.CreateCheckStatusResponse(request, instanceId);
    }

    [Function("CounterOrchestration")]
    public static async Task<int> RunOrchestrationAsync(
        [OrchestrationTrigger] TaskOrchestrationContext context, Payload input)
    {
        ILogger logger = context.CreateReplaySafeLogger<Counter>();
        int result = await context.Entities.CallEntityAsync<int>(input.Id, "add", input.Add);
        logger.LogInformation("Counter value: {Value}", result);
        return result;
    }

    public record Payload(EntityInstanceId Id, int Add);
}
