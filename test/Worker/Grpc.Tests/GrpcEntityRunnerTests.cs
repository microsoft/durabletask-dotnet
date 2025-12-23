// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

public class GrpcEntityRunnerTests
{
    const string TestInstanceId = "@instance_id@my_entity";
    const int DefaultExtendedSessionIdleTimeoutInSeconds = 30;
    static readonly Protobuf.OperationRequest setOperation = new() { RequestId = Guid.NewGuid().ToString(), Input = 1.ToString(), Operation = "Set" };
    static readonly Protobuf.OperationRequest addOperation = new() { RequestId = Guid.NewGuid().ToString(), Input = 1.ToString(), Operation = "Add" };

    [Fact]
    public async Task EmptyOrNullParameters_Throw_Exceptions()
    {
        Func<Task> act = async () =>
            await GrpcEntityRunner.LoadAndRunAsync(string.Empty, new SimpleEntity(), new ExtendedSessionsCache());
        await act.Should().ThrowExactlyAsync<ArgumentException>().WithParameterName("encodedEntityRequest");

        act = () =>
            GrpcEntityRunner.LoadAndRunAsync(null!, new SimpleEntity(), new ExtendedSessionsCache());
        await act.Should().ThrowExactlyAsync<ArgumentNullException>().WithParameterName("encodedEntityRequest");

        act = () =>
            GrpcEntityRunner.LoadAndRunAsync("request", null!, new ExtendedSessionsCache());
        await act.Should().ThrowExactlyAsync<ArgumentNullException>().WithParameterName("implementation");
    }

    [Fact]
    public async Task EmptyState_Returns_NeedsStateInResponse_Async()
    {
        using var extendedSessions = new ExtendedSessionsCache();

        // No state and without extended sessions enabled
        Protobuf.EntityBatchRequest entityRequest = CreateEntityRequest([]);
        entityRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(false) }});
        byte[] requestBytes = entityRequest.ToByteArray();
        string requestString =  Convert.ToBase64String(requestBytes);
        string responseString = await GrpcEntityRunner.LoadAndRunAsync(requestString, new SimpleEntity(), extendedSessions);
        Protobuf.EntityBatchResult response = Protobuf.EntityBatchResult.Parser.ParseFrom(Convert.FromBase64String(responseString));
        Assert.True(response.RequiresState);
        Assert.False(extendedSessions.IsInitialized);

        // No state but with extended sessions enabled
        entityRequest.Properties.Add(new MapField<string, Value>() {
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        requestBytes = entityRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        responseString = await GrpcEntityRunner.LoadAndRunAsync(requestString, new SimpleEntity(), extendedSessions);
        response = Protobuf.EntityBatchResult.Parser.ParseFrom(Convert.FromBase64String(responseString));
        Assert.True(response.RequiresState);
        Assert.True(extendedSessions.IsInitialized);
    }

    [Fact]
    public async Task MalformedRequestParameters_Means_CacheNotInitialized_Async()
    {
        using var extendedSessions = new ExtendedSessionsCache();
        Protobuf.EntityBatchRequest entityRequest = CreateEntityRequest([]);

        // Misspelled extended session timeout key
        entityRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(false) },
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionsIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        byte[] requestBytes = entityRequest.ToByteArray();
        string requestString = Convert.ToBase64String(requestBytes);
        await GrpcEntityRunner.LoadAndRunAsync(requestString, new SimpleEntity(), extendedSessions);
        Assert.False(extendedSessions.IsInitialized);

        // Wrong value type for extended session timeout key
        entityRequest.Properties.Clear();
        entityRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(false) },
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForString("hi") } });
        requestBytes = entityRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        await GrpcEntityRunner.LoadAndRunAsync(requestString, new SimpleEntity(), extendedSessions);
        Assert.False(extendedSessions.IsInitialized);

        // Invalid number for extended session timeout key (must be > 0)
        entityRequest.Properties.Clear();
        entityRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(false) },
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(0) } });
        requestBytes = entityRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        await GrpcEntityRunner.LoadAndRunAsync(requestString, new SimpleEntity(), extendedSessions);
        Assert.False(extendedSessions.IsInitialized);

        // Invalid number for extended session timeout key (must be > 0)
        entityRequest.Properties.Clear();
        entityRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(false) },
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(-1) } });
        requestBytes = entityRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        await GrpcEntityRunner.LoadAndRunAsync(requestString, new SimpleEntity(), extendedSessions);
        Assert.False(extendedSessions.IsInitialized);

        // No extended session timeout key
        entityRequest.Properties.Clear();
        entityRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(false) },
            { "IsExtendedSession", Value.ForBool(true) } });
        requestBytes = entityRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        await GrpcEntityRunner.LoadAndRunAsync(requestString, new SimpleEntity(), extendedSessions);
        Assert.False(extendedSessions.IsInitialized);

        // Misspelled extended session key
        entityRequest.Properties.Clear();
        entityRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(false) },
            { "isExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        requestBytes = entityRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        await GrpcEntityRunner.LoadAndRunAsync(requestString, new SimpleEntity(), extendedSessions);
        Assert.False(extendedSessions.IsInitialized);

        // Wrong value type for extended session key
        entityRequest.Properties.Clear();
        entityRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(false) },
            { "IsExtendedSession", Value.ForNumber(1) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        requestBytes = entityRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        await GrpcEntityRunner.LoadAndRunAsync(requestString, new SimpleEntity(), extendedSessions);
        Assert.False(extendedSessions.IsInitialized);

        // No extended session key
        entityRequest.Properties.Clear();
        entityRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(false) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        requestBytes = entityRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        await GrpcEntityRunner.LoadAndRunAsync(requestString, new SimpleEntity(), extendedSessions);
        Assert.False(extendedSessions.IsInitialized);
    }

    [Fact]
    public async Task IsExtendedSessionFalse_Means_NoExtendedSessionStored_Async()
    {
        using var extendedSessions = new ExtendedSessionsCache();
        Protobuf.EntityBatchRequest entityRequest = CreateEntityRequest([setOperation]);

        entityRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(false) },
            { "IsExtendedSession", Value.ForBool(false) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        byte[] requestBytes = entityRequest.ToByteArray();
        string requestString = Convert.ToBase64String(requestBytes);
        await GrpcEntityRunner.LoadAndRunAsync(requestString, new SimpleEntity(), extendedSessions);
        Assert.True(extendedSessions.IsInitialized);
        Assert.False(extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds).TryGetValue(TestInstanceId, out object? extendedSession));
    }

    /// <summary>
    /// These tests verify that a malformed/nonexistent "IncludeState" parameter means that the worker will attempt to 
    /// fulfill the entity request and not request a state for it. However, it is of course very undesirable that a 
    /// state is not attached to the request, but no state is requested by the worker due to a malformed "IncludeState" parameter
    /// even when it needs one to fulfill the request. This would need to be checked on whatever side is calling this SDK. 
    /// </summary>
    [Fact]
    public async Task MalformedIncludeStateParameter_Means_NoStateRequired_Async()
    {
        using var extendedSessions = new ExtendedSessionsCache();
        Protobuf.EntityBatchRequest entityRequest = CreateEntityRequest([addOperation], entityState: 1.ToString());

        // Misspelled include entity state key
        entityRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeSTate", Value.ForBool(false) },
            { "IsExtendedSession", Value.ForBool(false) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        byte[] requestBytes = entityRequest.ToByteArray();
        string requestString = Convert.ToBase64String(requestBytes);
        string responseString = await GrpcEntityRunner.LoadAndRunAsync(requestString, new SimpleEntity(), extendedSessions);
        Protobuf.EntityBatchResult response = Protobuf.EntityBatchResult.Parser.ParseFrom(Convert.FromBase64String(responseString));
        Assert.False(response.RequiresState);
        Assert.Equal("2", response.EntityState);

        // Wrong value type for include entity state key
        entityRequest.Properties.Clear();
        entityRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForString("no") },
            { "IsExtendedSession", Value.ForBool(false) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        requestBytes = entityRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        responseString = await GrpcEntityRunner.LoadAndRunAsync(requestString, new SimpleEntity(), extendedSessions);
        response = Protobuf.EntityBatchResult.Parser.ParseFrom(Convert.FromBase64String(responseString));
        Assert.False(response.RequiresState);
        Assert.Equal("2", response.EntityState);

        // No include entity state key
        entityRequest.Properties.Clear();
        entityRequest.Properties.Add(new MapField<string, Value>() {
            { "IsExtendedSession", Value.ForBool(false) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        requestBytes = entityRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        responseString = await GrpcEntityRunner.LoadAndRunAsync(requestString, new SimpleEntity(), extendedSessions);
        response = Protobuf.EntityBatchResult.Parser.ParseFrom(Convert.FromBase64String(responseString));
        Assert.False(response.RequiresState);
        Assert.Equal("2", response.EntityState);
    }

    [Fact]
    public async Task Entity_State_Stored_Async()
    {
        using var extendedSessions = new ExtendedSessionsCache();
        Protobuf.EntityBatchRequest entityRequest = CreateEntityRequest([setOperation]);
        entityRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(true) },
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        byte[] requestBytes = entityRequest.ToByteArray();
        string requestString = Convert.ToBase64String(requestBytes);
        string responseString = await GrpcEntityRunner.LoadAndRunAsync(requestString, new SimpleEntity(), extendedSessions);
        Protobuf.EntityBatchResult response = Protobuf.EntityBatchResult.Parser.ParseFrom(Convert.FromBase64String(responseString));
        Assert.True(extendedSessions.IsInitialized);
        Assert.True(extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds).TryGetValue(TestInstanceId, out object? extendedSession));
        Assert.Equal("1", extendedSession);
        Assert.Equal("1", response.EntityState);
    }

    [Fact]
    public async Task ExternallyEndedExtendedSession_Evicted_Async()
    {
        using var extendedSessions = new ExtendedSessionsCache();
        Protobuf.EntityBatchRequest entityRequest = CreateEntityRequest([setOperation]);
        entityRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(true) },
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        byte[] requestBytes = entityRequest.ToByteArray();
        string requestString = Convert.ToBase64String(requestBytes);
        string responseString = await GrpcEntityRunner.LoadAndRunAsync(requestString, new SimpleEntity(), extendedSessions);
        Protobuf.EntityBatchResult response = Protobuf.EntityBatchResult.Parser.ParseFrom(Convert.FromBase64String(responseString));
        Assert.True(extendedSessions.IsInitialized);
        Assert.True(extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds).TryGetValue(TestInstanceId, out object? extendedSession));
        Assert.Equal("1", extendedSession);
        Assert.Equal("1", response.EntityState);

        // Now set the extended session flag to false for this instance
        entityRequest.Properties.Clear();
        entityRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(true) },
            { "IsExtendedSession", Value.ForBool(false) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        requestBytes = entityRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        await GrpcEntityRunner.LoadAndRunAsync(requestString, new SimpleEntity(), extendedSessions);
        Assert.True(extendedSessions.IsInitialized);
        Assert.False(extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds).TryGetValue(TestInstanceId, out extendedSession));
    }

    [Fact]
    public async Task Stale_ExtendedSessions_Evicted_Async()
    {
        using var extendedSessions = new ExtendedSessionsCache();
        int extendedSessionIdleTimeout = 5;
        Protobuf.EntityBatchRequest entityRequest = CreateEntityRequest([setOperation]);
        entityRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(true) },
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(extendedSessionIdleTimeout) } });
        byte[] requestBytes = entityRequest.ToByteArray();
        string requestString = Convert.ToBase64String(requestBytes);
        string responseString = await GrpcEntityRunner.LoadAndRunAsync(requestString, new SimpleEntity(), extendedSessions);
        Protobuf.EntityBatchResult response = Protobuf.EntityBatchResult.Parser.ParseFrom(Convert.FromBase64String(responseString));
        Assert.True(extendedSessions.IsInitialized);
        Assert.True(extendedSessions.GetOrInitializeCache(extendedSessionIdleTimeout).TryGetValue(TestInstanceId, out object? extendedSession));
        Assert.Equal("1", extendedSession);
        Assert.Equal("1", response.EntityState);

        // Wait for longer than the timeout to account for finite cache scan for stale items frequency 
        await Task.Delay(extendedSessionIdleTimeout * 1000 * 2);
        Assert.False(extendedSessions.GetOrInitializeCache(extendedSessionIdleTimeout).TryGetValue(TestInstanceId, out extendedSession));

        // Now that the entry was evicted from the cache, the entity runner needs an entity state to complete the request
        entityRequest.Properties.Clear();
        entityRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(false) },
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(extendedSessionIdleTimeout) } });
        requestBytes = entityRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        responseString = await GrpcEntityRunner.LoadAndRunAsync(requestString, new SimpleEntity(), extendedSessions);
        response = Protobuf.EntityBatchResult.Parser.ParseFrom(Convert.FromBase64String(responseString));
        Assert.True(response.RequiresState);
    }

    [Fact]
    public async Task EntityStateIncluded_Means_ExtendedSession_Evicted_Async()
    {
        using var extendedSessions = new ExtendedSessionsCache();
        Protobuf.EntityBatchRequest entityRequest = CreateEntityRequest([addOperation], entityState: 1.ToString());
        entityRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(true) },
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        byte[] requestBytes = entityRequest.ToByteArray();
        string requestString = Convert.ToBase64String(requestBytes);
        await GrpcEntityRunner.LoadAndRunAsync(requestString, new SimpleEntity(), extendedSessions);
        Assert.True(extendedSessions.IsInitialized);
        Assert.True(extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds).TryGetValue(TestInstanceId, out object? extendedSession));

        // Now we will retry the same request, but with a different value for the state. If the extended session is not evicted, then the result will be incorrect.
        entityRequest = CreateEntityRequest([addOperation], entityState: 5.ToString());
        entityRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(true) },
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        requestBytes = entityRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        string responseString = await GrpcEntityRunner.LoadAndRunAsync(requestString, new SimpleEntity(), extendedSessions);
        Protobuf.EntityBatchResult response = Protobuf.EntityBatchResult.Parser.ParseFrom(Convert.FromBase64String(responseString));
        Assert.True(extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds).TryGetValue(TestInstanceId, out extendedSession));
        Assert.Equal("6", extendedSession);
        Assert.Equal("6", response.EntityState);
    }

    [Fact]
    public async Task Null_ExtendedSessionsCache_IsOkay_Async()
    {
        Protobuf.EntityBatchRequest entityRequest = CreateEntityRequest([setOperation]);

        // Set up the parameters as if extended sessions are enabled, but do not pass an extended session cache to the request.
        // The request should still be successful.
        entityRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(true) },
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        byte[] requestBytes = entityRequest.ToByteArray();
        string requestString = Convert.ToBase64String(requestBytes);
        string responseString = await GrpcEntityRunner.LoadAndRunAsync(requestString, new SimpleEntity());
        Protobuf.EntityBatchResult response = Protobuf.EntityBatchResult.Parser.ParseFrom(Convert.FromBase64String(responseString));
        Assert.Equal("1", response.EntityState);

        // Now try it again without any properties specified. The request should still be successful.
        entityRequest.Properties.Clear();
        requestBytes = entityRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        responseString = await GrpcEntityRunner.LoadAndRunAsync(requestString, new SimpleEntity());
        response = Protobuf.EntityBatchResult.Parser.ParseFrom(Convert.FromBase64String(responseString));
        Assert.Equal("1", response.EntityState);
    }

    static Protobuf.EntityBatchRequest CreateEntityRequest(IEnumerable<Protobuf.OperationRequest> requests, string? entityState = null)
    {
        var entityBatchRequest = new Protobuf.EntityBatchRequest()
        {
            InstanceId = TestInstanceId,
            EntityState = entityState,
            Operations = { requests }
        };
        return entityBatchRequest;
    }

    sealed class SimpleEntity : TaskEntity<int?>
    {
        public void Set(int value)
        {
            this.State = value;
        }

        public void Add(int value)
        {
            this.State += value;
        }

        public void Delete()
        {
            this.State = null;
        }
    }
}
