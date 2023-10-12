// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace AzureFunctionsApp.Entities;

/**
* The counter example shows the 3 different ways to dispatch to an entity.
* The mode query string is what controls this:
* mode=0 or mode=entity (default) - dispatch to "counter" entity
* mode=1 or mode=state - dispatch to "counter_state" entity
* mode=2 or mode=static - dispatch to "counter_alt" entity
*
* "counter" and "counter_alt" are the same entities, however they use
* two different functions to dispatch, and thus are different entities when
* persisted in the backend.
*
* See "counters.http" file for HTTP examples.
*/

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

    public int Get() => this.State;

    public void Reset() => this.State = 0;

    [Function("Counter")]
    public Task DispatchAsync([EntityTrigger] TaskEntityDispatcher dispatcher)
    {
        // Can dispatch to a TaskEntity<TState> by passing a instance.
        return dispatcher.DispatchAsync(this);
    }

    [Function("Counter_Alt")]
    public static Task DispatchStaticAsync([EntityTrigger] TaskEntityDispatcher dispatcher)
    {
        // Can also dispatch to a TaskEntity<TState> by using a static method.
        // However, this is a different entity ID - "counter_alt" and not "counter". Even though it uses the same
        // entity implementation, the function attribute has a different name, which determines the entity ID.
        return dispatcher.DispatchAsync<Counter>();
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

    public int Get() => this.Value;

    public void Reset() => this.Value = 0;

    [Function("Counter_State")]
    public static Task DispatchAsync([EntityTrigger] TaskEntityDispatcher dispatcher)
    {
        // Using the dispatch to a state object will deserialize the state directly to that instance and dispatch to an
        // appropriate method.
        // Can only dispatch to a state object via generic argument.
        // "state object" is defined as any type which does not implement ITaskEntity.
        return dispatcher.DispatchAsync<StateCounter>();
    }
}

public static class CounterApis
{
    /// <summary>
    /// Usage:
    /// Add to <see cref="Counter"/>:
    /// POST /api/counters/{id}/add/{value}
    /// POST /api/counters/{id}/add/{value}?&mode=0
    /// POST /api/counters/{id}/add/{value}?&mode=entity
    /// 
    /// Add to <see cref="StateCounter"/>
    /// POST /api/counters/{id}/add/{value}?&mode=1
    /// POST /api/counters/{id}/add/{value}?&mode=state
    /// 
    /// Add to <see cref="Counter"/>, using the static method.
    /// POST /api/counters/{id}/add/{value}?&mode=2
    /// POST /api/counters/{id}/add/{value}?&mode=static
    /// </summary>
    [Function("Counter_Add")]
    public static async Task<HttpResponseData> AddAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "counters/{id}/add/{value}")] HttpRequestData request,
        [DurableClient] DurableTaskClient client,
        string id,
        int value)
    {
        EntityInstanceId entityId = GetEntityId(request, id);
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
            "CounterOrchestration", new Payload(entityId, value));
        return client.CreateCheckStatusResponse(request, instanceId);
    }

    /// <summary>
    /// Usage:
    /// Add to <see cref="Counter"/>:
    /// GET /api/counters/{id}
    /// GET /api/counters/{id}?&mode=0
    /// GET /api/counters/{id}?&mode=entity
    /// 
    /// Add to <see cref="StateCounter"/>
    /// GET /api/counters/{id}?&mode=1
    /// GET /api/counters/{id}?&mode=state
    /// 
    /// Add to <see cref="Counter"/>, using the static method.
    /// GET /api/counters/{id}?&mode=2
    /// GET /api/counters/{id}?&mode=static
    /// </summary>
    [Function("Counter_Get")]
    public static async Task<HttpResponseData> GetAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "counters/{id}")] HttpRequestData request,
        [DurableClient] DurableTaskClient client,
        string id)
    {
        EntityInstanceId entityId = GetEntityId(request, id);

        // "counter_state" corresponds to StateCounter, which has a different state structure than the other modes.
        // The following calls highlight how entity vs state dispatch changes the state structure of your entity.
        object? entity = entityId.Name == "counter_state"
            ? await client.Entities.GetEntityAsync<StateCounter>(entityId)
            : await client.Entities.GetEntityAsync<int>(entityId);
        if (entity is null)
        {
            return request.CreateResponse(HttpStatusCode.NotFound);
        }

        // We serialize the entire entity to show the structural differences.
        HttpResponseData response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(entity);
        return response;
    }

    /// <summary>
    /// Usage:
    /// Add to <see cref="Counter"/>:
    /// DELETE /api/counters/{id}
    /// DELETE /api/counters/{id}?&mode=0
    /// DELETE /api/counters/{id}?&mode=entity
    /// 
    /// Add to <see cref="StateCounter"/>
    /// DELETE /api/counters/{id}?&mode=1
    /// DELETE /api/counters/{id}?&mode=state
    /// 
    /// Add to <see cref="Counter"/>, using the static method.
    /// DELETE /api/counters/{id}?&mode=2
    /// DELETE /api/counters/{id}?&mode=static
    /// </summary>
    [Function("Counter_Delete")]
    public static async Task<HttpResponseData> DeleteAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "counters/{id}")] HttpRequestData request,
        [DurableClient] DurableTaskClient client,
        string id)
    {
        EntityInstanceId entityId = GetEntityId(request, id);
        await client.Entities.SignalEntityAsync(entityId, "delete");
        return request.CreateResponse(HttpStatusCode.Accepted);
    }

    /// <summary>
    /// Usage:
    /// Add to <see cref="Counter"/>:
    /// POST /api/counters/{id}/reset
    /// POST /api/counters/{id}/reset?&mode=0
    /// POST /api/counters/{id}/reset?&mode=entity
    /// 
    /// Add to <see cref="StateCounter"/>
    /// POST /api/counters/{id}/reset?&mode=1
    /// POST /api/counters/{id}/reset?&mode=state
    /// 
    /// Add to <see cref="Counter"/>, using the static method.
    /// POST /api/counters/{id}/reset?&mode=2
    /// POST /api/counters/{id}/reset?&mode=static
    /// </summary>
    [Function("Counter_Reset")]
    public static async Task<HttpResponseData> ResetAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "counters/{id}/reset")] HttpRequestData request,
        [DurableClient] DurableTaskClient client,
        string id)
    {
        EntityInstanceId entityId = GetEntityId(request, id);
        await client.Entities.SignalEntityAsync(entityId, "reset");
        return request.CreateResponse(HttpStatusCode.Accepted);
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

    static EntityInstanceId GetEntityId(HttpRequestData request, string key)
    {
        // switch to Counter_State if ?mode=1 or ?mode=state is supplied.
        // or to Counter_Alt if ?mode=2 or ?mode=static is supplied.
        string name;
        string? mode = request.Query["mode"];
        if (int.TryParse(mode, out int m))
        {
            name = m switch
            {
                1 => "counter_state",
                2 => "counter_alt",
                _ => "counter",
            };
        }
        else
        {
            name = mode switch
            {
                "state" => "counter_state",
                "static" => "counter_alt",
                _ => "counter",
            };
        }

        return new(name, key);
    }
}
