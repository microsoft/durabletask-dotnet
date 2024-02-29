// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace AzureFunctionsApp.Entities;

public record User(string Name, int Age);

public record UserUpdate(string? Name, int? Age);

/// <summary>
/// This sample demonstrates how to bind to <see cref="TaskEntityContext"/> as well as dispatch to orchestrations.
/// </summary>
public class UserEntity : TaskEntity<User>
{
    readonly ILogger logger;

    public UserEntity(ILogger<UserEntity> logger)
    {
        this.logger = logger;
    }

    public void Set(User user)
    {
        User previous = this.State;
        this.State = user;
        this.logger.LogInformation("User {Id} set {Old} -> {New}", this.Context.Id.Key, previous, this.State);
    }

    public void Update(UserUpdate update)
    {
        (string n, int a) = (update.Name ?? this.State.Name, update.Age ?? this.State.Age);
        User previous = this.State;
        this.State = previous with { Name = n, Age = a };

        this.logger.LogInformation("User {Id} updated {Old} -> {New}", this.Context.Id.Key, previous, this.State);
    }

    /// <summary>
    /// Starts a <see cref="Greeting.GreetingOrchestration(TaskOrchestrationContext, Greeting.Input)"/>.
    /// </summary>
    /// <param name="context">The context object. This will allow for calling orchestrations.</param>
    /// <param name="message">
    /// The optional message. By using a default parameter supplying input to this operation is optional.
    /// </param>
    public void Greet(TaskEntityContext context, string? message = null)
    {
        if (this.State.Name is null)
        {
            this.logger.LogError("User is not in a valid state for a greet operation.");
            throw new InvalidOperationException("User has not been initialized.");
        }

        // Get access to TaskEntityContext by adding it as a parameter. Can be with or without an input parameter.
        // Order does not matter.
        Greeting.Input input = new(this.State.Name, this.State.Age) { CustomMessage = message };
        context.ScheduleNewOrchestration(nameof(Greeting.GreetingOrchestration), input);

        // When using TaskEntity<TState>, the TaskEntityContext can also be accessed via this.Context.
        // this.Context.ScheduleNewOrchestration(nameof(Greeting.GreetingOrchestration), input);
    }

    [Function(nameof(User))]
    public Task DispatchAsync([EntityTrigger] TaskEntityDispatcher dispatcher)
    {
        // Can dispatch to a TaskEntity<TState> by passing a instance.
        return dispatcher.DispatchAsync(this);
    }

    protected override User InitializeState(TaskEntityOperation entityOperation)
    {
        // No parameterless constructor, must initialize state manually.
        return new(null!, -1);
    }
}

/// <summary>
/// APIs:
/// Create User: PUT /api/users/{id}?name={name}&age={age} -- both name and age are required
/// Update User: PATCH /api/users/{id}?name={name}&age={age} -- either name or age can be updated
/// Get User: GET /api/users/{id}
/// Delete User: DELETE /api/users/{id}
/// Greet User: POST /api/users/{id}/greet?message={message}
/// </summary>
public static class UserApis
{
    [Function("PutUser")]
    public static async Task<HttpResponseData> PutAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "users/{id}")] HttpRequestData request,
        [DurableClient] DurableTaskClient client,
        string id)
    {
        if (request.Query["name"] is not string name
            || !int.TryParse(request.Query["age"], out int age))
        {
            HttpResponseData response = request.CreateResponse(HttpStatusCode.BadRequest);
            await response.WriteStringAsync("Both name and age must be provided.");
            return response;
        }

        if (age < 0)
        {
            HttpResponseData response = request.CreateResponse(HttpStatusCode.BadRequest);
            await response.WriteStringAsync("Age must be a positive integer.");
            return response;
        }

        await client.Entities.SignalEntityAsync(new EntityInstanceId(nameof(User), id), "set", new User(name, age));
        return request.CreateResponse(HttpStatusCode.Accepted);
    }

    [Function("PatchUser")]
    public static async Task<HttpResponseData> PatchAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "users/{id}")] HttpRequestData request,
        [DurableClient] DurableTaskClient client,
        string id)
    {
        bool hasAge = int.TryParse(request.Query["age"], out int age);
        if (age < 0)
        {
            HttpResponseData response = request.CreateResponse(HttpStatusCode.BadRequest);
            await response.WriteStringAsync("Age must be a positive integer.");
            return response;
        }

        UserUpdate update = new(request.Query["name"], hasAge ? age : null);

        await client.Entities.SignalEntityAsync(new EntityInstanceId(nameof(User), id), "Update", update);
        return request.CreateResponse(HttpStatusCode.Accepted);
    }

    [Function("DeleteUser")]
    public static async Task<HttpResponseData> DeleteAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "users/{id}")] HttpRequestData request,
        [DurableClient] DurableTaskClient client,
        string id)
    {
        // Even though UserEntity does not have 'delete' method on it, the base class TaskEntity<TState> will handle it.
        await client.Entities.SignalEntityAsync(new EntityInstanceId(nameof(User), id), "delete");
        return request.CreateResponse(HttpStatusCode.Accepted);
    }

    [Function("GetUser")]
    public static async Task<HttpResponseData> GetAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users/{id}")] HttpRequestData request,
        [DurableClient] DurableTaskClient client,
        string id)
    {
        EntityMetadata<User>? entity = await client.Entities.GetEntityAsync<User>(
            new EntityInstanceId(nameof(User), id));
        if (entity is null)
        {
            return request.CreateResponse(HttpStatusCode.NotFound);
        }

        HttpResponseData response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(entity.State);
        return response;
    }

    [Function("GreetUser")]
    public static async Task<HttpResponseData> GreetAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users/{id}/greet")] HttpRequestData request,
        [DurableClient] DurableTaskClient client,
        string id)
    {
        string? message = request.Query["message"];
        await client.Entities.SignalEntityAsync(new EntityInstanceId(nameof(User), id), "greet", message);
        return request.CreateResponse(HttpStatusCode.Accepted);
    }
}
