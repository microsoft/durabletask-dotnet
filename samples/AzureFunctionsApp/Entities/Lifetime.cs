// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace AzureFunctionsApp.Entities;

/// <summary>
/// Example showing the lifetime of an entity. An entity is initialized on the first operation it receives and then
/// is considered deleted when <see cref="TaskEntity{TState}.State"/> is <c>null</c> at the end of an operation. It
/// is also possible to design an entity which remains stateless by always returning <c>null</c> from
/// <see cref="InitializeState"/> and never assigning a non-null state.
/// </summary>
public class Lifetime(ILogger<Lifetime> logger) : TaskEntity<MyState>
{
    /// <summary>
    /// Optional property to override. When 'true', this will allow dispatching of operations to the <see cref="State">
    /// object if there is no matching method on the entity. Default is 'false'.
    /// Stated differently: if this is <c>true</c> and there is no matching method for a given operation on the entity
    /// type, then the operation will attempt to find a matching method on <typeparamref name="TState"/> instead.
    /// </summary>
    protected override bool AllowStateDispatch => base.AllowStateDispatch;

    // NOTE: when using TaskEntity<TState>, you cannot use "RunAsync" as the entity trigger name as this conflicts
    // with the base class method 'RunAsync'.
    [Function(nameof(Lifetime))]
    public Task DispatchAsync([EntityTrigger] TaskEntityDispatcher dispatcher)
    {
        logger.LogInformation("Dispatching entity");
        return dispatcher.DispatchAsync(this);
    }

    public MyState Get() => this.State;

    public void Init() { } // no op just to initialize this entity.

    public void CustomDelete()
    {
        // This method shows that entity deletion can be accomplished from any operation by nulling out the state. The
        // operation does not have to be named "delete". The only requirement for deletion is that state is <c>null</c>
        // when the operation returns.
        // The '!' in `null!;` is only needed because C# explicit nullability is enabled.
        // This can be avoided by either:
        // 1) Declare TaskEntity<MyState?> instead.
        // 2) Disable explicit nullability.
        this.State = null!;
    }

    public void Delete()
    {
        // Entities have an implicit 'delete' operation when there is no matching 'delete' method. By explicitly adding
        // a 'Delete' method, it will override the implicit 'delete' operation.
        // Since state deletion is determined by nulling out <c>this.State</c>, it means that value-types cannot be
        // deleted except by the implicit delete (which will still delete it). To manually delete a value-type, the
        // state can be declared as nullable. Such as TaskEntity<int?> instead of TaskEntity<int>.
        this.State = null!;
    }

    protected override MyState InitializeState(TaskEntityOperation operation)
    {
        // This method allows for customizing the default state value for a new entity.
        return new(Guid.NewGuid().ToString("N"), Random.Shared.Next(0, 1000));
    }
}

public static class LifetimeApis
{
    [Function("Lifetime_Get")]
    public static async Task<HttpResponseData> GetAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "lifetimes/{id}")] HttpRequestData request,
        [DurableClient] DurableTaskClient client,
        string id)
    {
        EntityMetadata<MyState>? entity = await client.Entities.GetEntityAsync<MyState>(
            new EntityInstanceId(nameof(Lifetime), id));

        if (entity is null)
        {
            return request.CreateResponse(HttpStatusCode.NotFound);
        }

        HttpResponseData response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(entity);
        return response;
    }

    [Function("Lifetime_Init")]
    public static async Task<HttpResponseData> InitAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "lifetimes/{id}")] HttpRequestData request,
        [DurableClient] DurableTaskClient client,
        string id)
    {
        await client.Entities.SignalEntityAsync(new EntityInstanceId(nameof(Lifetime), id), "init");
        return request.CreateResponse(HttpStatusCode.Accepted);
    }

    [Function("Lifetime_Delete")]
    public static async Task<HttpResponseData> DeleteAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "lifetimes/{id}")] HttpRequestData request,
        [DurableClient] DurableTaskClient client,
        string id)
    {
        string operation = bool.TryParse(request.Query["custom"], out bool b) && b ? "customDelete" : "delete";
        await client.Entities.SignalEntityAsync(new EntityInstanceId(nameof(Lifetime), id), operation);
        return request.CreateResponse(HttpStatusCode.Accepted);
    }
}

public record MyState(string PropA, int PropB);
