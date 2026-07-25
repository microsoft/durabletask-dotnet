// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using System.Security.Cryptography;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

public class GrpcOrchestrationRunnerTests
{
    const string TestInstanceId = "instance_id";
    const string TestExecutionId = "execution_id";
    const int DefaultExtendedSessionIdleTimeoutInSeconds = 30;

    [Fact]
    public void EmptyOrNullParameters_Throw_Exceptions()
    {
        Action act = () =>
            GrpcOrchestrationRunner.LoadAndRun(string.Empty, new SimpleOrchestrator(), new ExtendedSessionsCache());
        act.Should().ThrowExactly<ArgumentException>().WithParameterName("encodedOrchestratorRequest");

        act = () =>
            GrpcOrchestrationRunner.LoadAndRun(null!, new SimpleOrchestrator(), new ExtendedSessionsCache());
        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("encodedOrchestratorRequest");

        act = () =>
            GrpcOrchestrationRunner.LoadAndRun("request", null!, new ExtendedSessionsCache());
        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("implementation");
    }

    [Fact]
    public void EmptyHistory_Returns_NeedsHistoryInResponse()
    {
        using var extendedSessions = new ExtendedSessionsCache();

        // No history and without extended sessions enabled
        Protobuf.OrchestratorRequest orchestratorRequest = CreateOrchestratorRequest([]);
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(false) }});
        byte[] requestBytes = orchestratorRequest.ToByteArray();
        string requestString = Convert.ToBase64String(requestBytes);
        string responseString = GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator(), extendedSessions);
        Protobuf.OrchestratorResponse response = Protobuf.OrchestratorResponse.Parser.ParseFrom(Convert.FromBase64String(responseString));
        Assert.True(response.RequiresHistory);
        Assert.False(extendedSessions.IsInitialized);

        // No history but with extended sessions enabled
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        requestBytes = orchestratorRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        responseString = GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator(), extendedSessions);
        response = Protobuf.OrchestratorResponse.Parser.ParseFrom(Convert.FromBase64String(responseString));
        Assert.True(response.RequiresHistory);
        Assert.True(extendedSessions.IsInitialized);
    }

    [Fact]
    public void NullExtendedSessionStored_Means_ExtendedSessionNotUsed()
    {
        using var extendedSessions = new ExtendedSessionsCache();
        extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds).Set<ExtendedSessionState>(
            TestInstanceId,
            null!,
            new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromSeconds(DefaultExtendedSessionIdleTimeoutInSeconds) });

        // No history, so response indicates that a history is required
        Protobuf.OrchestratorRequest orchestratorRequest = CreateOrchestratorRequest([]);
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(false) },
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        byte[] requestBytes = orchestratorRequest.ToByteArray();
        string requestString = Convert.ToBase64String(requestBytes);
        string responseString = GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator(), extendedSessions);
        Protobuf.OrchestratorResponse response = Protobuf.OrchestratorResponse.Parser.ParseFrom(Convert.FromBase64String(responseString));
        Assert.True(response.RequiresHistory);

        // History provided so the request can be fulfilled and an extended session is stored
        var historyEvent = new Protobuf.HistoryEvent
        {
            EventId = -1,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ExecutionStarted = new Protobuf.ExecutionStartedEvent()
            {
                OrchestrationInstance = new Protobuf.OrchestrationInstance
                {
                    InstanceId = TestInstanceId,
                    ExecutionId = TestExecutionId,
                },
            }
        };
        orchestratorRequest = CreateOrchestratorRequest([historyEvent]);
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(true) },
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        requestBytes = orchestratorRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        responseString = GrpcOrchestrationRunner.LoadAndRun(requestString, new CallSubOrchestrationOrchestrator(), extendedSessions);
        response = Protobuf.OrchestratorResponse.Parser.ParseFrom(Convert.FromBase64String(responseString));
        Assert.False(response.RequiresHistory);
        Assert.True(extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds).TryGetValue(TestInstanceId, out object? extendedSession));
        Assert.NotNull(extendedSession);
    }

    [Fact]
    public void MalformedRequestParameters_Means_CacheNotInitialized()
    {
        using var extendedSessions = new ExtendedSessionsCache();
        Protobuf.OrchestratorRequest orchestratorRequest = CreateOrchestratorRequest([]);

        // Misspelled extended session timeout key
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(false) },
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionsIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        byte[] requestBytes = orchestratorRequest.ToByteArray();
        string requestString = Convert.ToBase64String(requestBytes);
        GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator(), extendedSessions);
        Assert.False(extendedSessions.IsInitialized);

        // Wrong value type for extended session timeout key
        orchestratorRequest.Properties.Clear();
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(false) },
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForString("hi") } });
        requestBytes = orchestratorRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator(), extendedSessions);
        Assert.False(extendedSessions.IsInitialized);

        // Invalid number for extended session timeout key (must be > 0)
        orchestratorRequest.Properties.Clear();
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(false) },
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(0) } });
        requestBytes = orchestratorRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator(), extendedSessions);
        Assert.False(extendedSessions.IsInitialized);

        // Invalid number for extended session timeout key (must be > 0)
        orchestratorRequest.Properties.Clear();
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(false) },
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(-1) } });
        requestBytes = orchestratorRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator(), extendedSessions);
        Assert.False(extendedSessions.IsInitialized);

        // No extended session timeout key
        orchestratorRequest.Properties.Clear();
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(false) },
            { "IsExtendedSession", Value.ForBool(true) } });
        requestBytes = orchestratorRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator(), extendedSessions);
        Assert.False(extendedSessions.IsInitialized);

        // Misspelled extended session key
        orchestratorRequest.Properties.Clear();
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(false) },
            { "isExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        requestBytes = orchestratorRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator(), extendedSessions);
        Assert.False(extendedSessions.IsInitialized);

        // Wrong value type for extended session key
        orchestratorRequest.Properties.Clear();
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(false) },
            { "IsExtendedSession", Value.ForNumber(1) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        requestBytes = orchestratorRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator(), extendedSessions);
        Assert.False(extendedSessions.IsInitialized);

        // No extended session key
        orchestratorRequest.Properties.Clear();
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(false) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        requestBytes = orchestratorRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator(), extendedSessions);
        Assert.False(extendedSessions.IsInitialized);
    }

    [Fact]
    public void IsExtendedSessionFalse_Means_NoExtendedSessionStored()
    {
        using var extendedSessions = new ExtendedSessionsCache();
        Protobuf.OrchestratorRequest orchestratorRequest = CreateOrchestratorRequest([]);

        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(false) },
            { "IsExtendedSession", Value.ForBool(false) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        byte[] requestBytes = orchestratorRequest.ToByteArray();
        string requestString = Convert.ToBase64String(requestBytes);
        GrpcOrchestrationRunner.LoadAndRun(requestString, new CallSubOrchestrationOrchestrator(), extendedSessions);
        Assert.True(extendedSessions.IsInitialized);
        Assert.False(extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds).TryGetValue(TestInstanceId, out object? extendedSession));
    }

    /// <summary>
    /// These tests verify that a malformed/nonexistent "IncludeState" parameter means that the worker will attempt to 
    /// fulfill the orchestration request and not request a history for it. However, it is of course very undesirable that a 
    /// history is not attached to the request, but no history is requested by the worker due to a malformed "IncludeState" parameter
    /// even when it needs one to fulfill the request. This would need to be checked on whatever side is calling this SDK. 
    /// </summary>
    [Fact]
    public void MalformedIncludeStateParameter_Means_NoHistoryRequired()
    {
        using var extendedSessions = new ExtendedSessionsCache();
        var historyEvent = new Protobuf.HistoryEvent
        {
            EventId = -1,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ExecutionStarted = new Protobuf.ExecutionStartedEvent()
            {
                OrchestrationInstance = new Protobuf.OrchestrationInstance
                {
                    InstanceId = TestInstanceId,
                    ExecutionId = TestExecutionId,
                },
            }
        };
        Protobuf.OrchestratorRequest orchestratorRequest = CreateOrchestratorRequest([historyEvent]);

        // Misspelled include past events key
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeSTate", Value.ForBool(false) },
            { "IsExtendedSession", Value.ForBool(false) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        byte[] requestBytes = orchestratorRequest.ToByteArray();
        string requestString = Convert.ToBase64String(requestBytes);
        string responseString = GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator(), extendedSessions);
        Protobuf.OrchestratorResponse response = Protobuf.OrchestratorResponse.Parser.ParseFrom(Convert.FromBase64String(responseString));
        Assert.False(response.RequiresHistory);

        // Wrong value type for include past events key
        orchestratorRequest.Properties.Clear();
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForString("no") },
            { "IsExtendedSession", Value.ForBool(false) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        requestBytes = orchestratorRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        responseString = GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator(), extendedSessions);
        response = Protobuf.OrchestratorResponse.Parser.ParseFrom(Convert.FromBase64String(responseString));
        Assert.False(response.RequiresHistory);

        // No include past events key
        orchestratorRequest.Properties.Clear();
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IsExtendedSession", Value.ForBool(false) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        requestBytes = orchestratorRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        responseString = GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator(), extendedSessions);
        response = Protobuf.OrchestratorResponse.Parser.ParseFrom(Convert.FromBase64String(responseString));
        Assert.False(response.RequiresHistory);
    }

    [Fact]
    public void Incomplete_Orchestration_Stored()
    {
        using var extendedSessions = new ExtendedSessionsCache();
        var historyEvent = new Protobuf.HistoryEvent
        {
            EventId = -1,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ExecutionStarted = new Protobuf.ExecutionStartedEvent()
            {
                OrchestrationInstance = new Protobuf.OrchestrationInstance
                {
                    InstanceId = TestInstanceId,
                    ExecutionId = TestExecutionId,
                },
            }
        };
        Protobuf.OrchestratorRequest orchestratorRequest = CreateOrchestratorRequest([historyEvent]);
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(true) },
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        byte[] requestBytes = orchestratorRequest.ToByteArray();
        string requestString = Convert.ToBase64String(requestBytes);
        GrpcOrchestrationRunner.LoadAndRun(requestString, new CallSubOrchestrationOrchestrator(), extendedSessions);
        Assert.True(extendedSessions.IsInitialized);
        Assert.True(extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds).TryGetValue(TestInstanceId, out object? extendedSession));
    }

    [Fact]
    public void Complete_Orchestration_NotStored()
    {
        using var extendedSessions = new ExtendedSessionsCache();
        var historyEvent = new Protobuf.HistoryEvent
        {
            EventId = -1,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ExecutionStarted = new Protobuf.ExecutionStartedEvent()
            {
                OrchestrationInstance = new Protobuf.OrchestrationInstance
                {
                    InstanceId = TestInstanceId,
                    ExecutionId = TestExecutionId,
                },
            }
        };
        Protobuf.OrchestratorRequest orchestratorRequest = CreateOrchestratorRequest([historyEvent]);
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(true) },
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        byte[] requestBytes = orchestratorRequest.ToByteArray();
        string requestString = Convert.ToBase64String(requestBytes);
        GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator(), extendedSessions);
        Assert.True(extendedSessions.IsInitialized);
        Assert.False(extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds).TryGetValue(TestInstanceId, out object? extendedSession));
    }

    [Fact]
    public void ExternallyEndedExtendedSession_Evicted()
    {
        using var extendedSessions = new ExtendedSessionsCache();
        var historyEvent = new Protobuf.HistoryEvent
        {
            EventId = -1,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ExecutionStarted = new Protobuf.ExecutionStartedEvent()
            {
                OrchestrationInstance = new Protobuf.OrchestrationInstance
                {
                    InstanceId = TestInstanceId,
                    ExecutionId = TestExecutionId,
                },
            }
        };
        Protobuf.OrchestratorRequest orchestratorRequest = CreateOrchestratorRequest([historyEvent]);
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(true) },
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        byte[] requestBytes = orchestratorRequest.ToByteArray();
        string requestString = Convert.ToBase64String(requestBytes);
        GrpcOrchestrationRunner.LoadAndRun(requestString, new CallSubOrchestrationOrchestrator(), extendedSessions);
        Assert.True(extendedSessions.IsInitialized);
        Assert.True(extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds).TryGetValue(TestInstanceId, out object? extendedSession));

        // Now set the extended session flag to false for this instance
        orchestratorRequest.Properties.Clear();
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(true) },
            { "IsExtendedSession", Value.ForBool(false) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        requestBytes = orchestratorRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        GrpcOrchestrationRunner.LoadAndRun(requestString, new CallSubOrchestrationOrchestrator(), extendedSessions);
        Assert.True(extendedSessions.IsInitialized);
        Assert.False(extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds).TryGetValue(TestInstanceId, out extendedSession));
    }

    [Fact]
    public async Task Stale_ExtendedSessions_Evicted_Async()
    {
        using var extendedSessions = new ExtendedSessionsCache();
        int extendedSessionIdleTimeout = 5;
        var historyEvent = new Protobuf.HistoryEvent
        {
            EventId = -1,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ExecutionStarted = new Protobuf.ExecutionStartedEvent()
            {
                OrchestrationInstance = new Protobuf.OrchestrationInstance
                {
                    InstanceId = TestInstanceId,
                    ExecutionId = TestExecutionId,
                },
            }
        };
        Protobuf.OrchestratorRequest orchestratorRequest = CreateOrchestratorRequest([historyEvent]);
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(true) },
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(extendedSessionIdleTimeout) } });
        byte[] requestBytes = orchestratorRequest.ToByteArray();
        string requestString = Convert.ToBase64String(requestBytes);
        GrpcOrchestrationRunner.LoadAndRun(requestString, new CallSubOrchestrationOrchestrator(), extendedSessions);
        Assert.True(extendedSessions.IsInitialized);
        Assert.True(extendedSessions.GetOrInitializeCache(extendedSessionIdleTimeout).TryGetValue(TestInstanceId, out object? extendedSession));

        // Wait for longer than the timeout to account for finite cache scan for stale items frequency 
        await Task.Delay(extendedSessionIdleTimeout * 1000 * 2);
        Assert.False(extendedSessions.GetOrInitializeCache(extendedSessionIdleTimeout).TryGetValue(TestInstanceId, out extendedSession));

        // Now that the entry was evicted from the cache, the orchestration runner needs an orchestration history to complete the request
        orchestratorRequest.Properties.Clear();
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(false) },
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(extendedSessionIdleTimeout) } });
        requestBytes = orchestratorRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        string responseString = GrpcOrchestrationRunner.LoadAndRun(requestString, new CallSubOrchestrationOrchestrator(), extendedSessions);
        Protobuf.OrchestratorResponse response = Protobuf.OrchestratorResponse.Parser.ParseFrom(Convert.FromBase64String(responseString));
        Assert.True(response.RequiresHistory);
    }

    [Fact]
    public void PastEventIncluded_Means_ExtendedSession_Evicted()
    {
        using var extendedSessions = new ExtendedSessionsCache();
        var historyEvent = new Protobuf.HistoryEvent
        {
            EventId = -1,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ExecutionStarted = new Protobuf.ExecutionStartedEvent()
            {
                OrchestrationInstance = new Protobuf.OrchestrationInstance
                {
                    InstanceId = TestInstanceId,
                    ExecutionId = TestExecutionId,
                },
            }
        };
        Protobuf.OrchestratorRequest orchestratorRequest = CreateOrchestratorRequest([historyEvent]);
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(true) },
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        byte[] requestBytes = orchestratorRequest.ToByteArray();
        string requestString = Convert.ToBase64String(requestBytes);
        GrpcOrchestrationRunner.LoadAndRun(requestString, new CallSubOrchestrationOrchestrator(), extendedSessions);
        Assert.True(extendedSessions.IsInitialized);
        Assert.True(extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds).TryGetValue(TestInstanceId, out object? extendedSession));

        // Now we will retry the same exact request. If the extended session is not evicted, then the request will fail due to duplicate ExecutionStarted events being detected
        // If the extended session is evicted because IncludeState is true, then the request will succeed and a new extended session will be stored
        GrpcOrchestrationRunner.LoadAndRun(requestString, new CallSubOrchestrationOrchestrator(), extendedSessions);
        Assert.True(extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds).TryGetValue(TestInstanceId, out extendedSession));
    }

    [Fact]
    public void ExternallyEndedExtendedSession_Evicted_DisposesCachedShimResources()
    {
        // Regression test for the round-3 lifecycle fix: the extended-session MemoryCache must dispose
        // the cached shim (and, transitively, its wrapper's cached SHA1 backing NewGuid()) whenever an
        // entry is evicted -- not just when the shim is replaced within a single Execute() call.
        using var extendedSessions = new ExtendedSessionsCache();
        var historyEvent = new Protobuf.HistoryEvent
        {
            EventId = -1,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ExecutionStarted = new Protobuf.ExecutionStartedEvent()
            {
                OrchestrationInstance = new Protobuf.OrchestrationInstance
                {
                    InstanceId = TestInstanceId,
                    ExecutionId = TestExecutionId,
                },
            }
        };
        Protobuf.OrchestratorRequest orchestratorRequest = CreateOrchestratorRequest([historyEvent]);
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(true) },
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        byte[] requestBytes = orchestratorRequest.ToByteArray();
        string requestString = Convert.ToBase64String(requestBytes);
        GrpcOrchestrationRunner.LoadAndRun(requestString, new NewGuidThenCallSubOrchestrationOrchestrator(), extendedSessions);
        Assert.True(extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds).TryGetValue(TestInstanceId, out object? extendedSession));
        SHA1 cachedHashAlgorithm = GetCachedHashAlgorithm(extendedSession!);

        // Now set the extended session flag to false for this instance, which removes/evicts the cache
        // entry and should synchronously invoke the eviction callback that disposes the cached shim.
        orchestratorRequest.Properties.Clear();
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(true) },
            { "IsExtendedSession", Value.ForBool(false) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        requestBytes = orchestratorRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        GrpcOrchestrationRunner.LoadAndRun(requestString, new NewGuidThenCallSubOrchestrationOrchestrator(), extendedSessions);
        Assert.False(extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds).TryGetValue(TestInstanceId, out _));

        Action useAfterDispose = () => cachedHashAlgorithm.ComputeHash(new byte[] { 1, 2, 3 });
        useAfterDispose.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task Stale_ExtendedSession_Evicted_DisposesCachedShimResources_Async()
    {
        // Regression test for the round-3 lifecycle fix: sliding-expiration eviction of a stale
        // extended session must also dispose the cached shim's resources.
        using var extendedSessions = new ExtendedSessionsCache();
        int extendedSessionIdleTimeout = 5;
        var historyEvent = new Protobuf.HistoryEvent
        {
            EventId = -1,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ExecutionStarted = new Protobuf.ExecutionStartedEvent()
            {
                OrchestrationInstance = new Protobuf.OrchestrationInstance
                {
                    InstanceId = TestInstanceId,
                    ExecutionId = TestExecutionId,
                },
            }
        };
        Protobuf.OrchestratorRequest orchestratorRequest = CreateOrchestratorRequest([historyEvent]);
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(true) },
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(extendedSessionIdleTimeout) } });
        byte[] requestBytes = orchestratorRequest.ToByteArray();
        string requestString = Convert.ToBase64String(requestBytes);
        GrpcOrchestrationRunner.LoadAndRun(requestString, new NewGuidThenCallSubOrchestrationOrchestrator(), extendedSessions);
        Assert.True(extendedSessions.GetOrInitializeCache(extendedSessionIdleTimeout).TryGetValue(TestInstanceId, out object? extendedSession));
        SHA1 cachedHashAlgorithm = GetCachedHashAlgorithm(extendedSession!);

        // Wait for longer than the timeout to account for finite cache scan for stale items frequency
        await Task.Delay(extendedSessionIdleTimeout * 1000 * 2);
        Assert.False(extendedSessions.GetOrInitializeCache(extendedSessionIdleTimeout).TryGetValue(TestInstanceId, out _));

        Action useAfterDispose = () => cachedHashAlgorithm.ComputeHash(new byte[] { 1, 2, 3 });
        useAfterDispose.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Null_ExtendedSessionsCache_IsOkay()
    {
        var historyEvent = new Protobuf.HistoryEvent
        {
            EventId = -1,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ExecutionStarted = new Protobuf.ExecutionStartedEvent()
            {
                OrchestrationInstance = new Protobuf.OrchestrationInstance
                {
                    InstanceId = TestInstanceId,
                    ExecutionId = TestExecutionId,
                },
            }
        };
        Protobuf.OrchestratorRequest orchestratorRequest = CreateOrchestratorRequest([historyEvent]);

        // Set up the parameters as if extended sessions are enabled, but do not pass an extended session cache to the request.
        // The request should still be successful.
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(true) },
            { "IsExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        byte[] requestBytes = orchestratorRequest.ToByteArray();
        string requestString = Convert.ToBase64String(requestBytes);
        string responseString = GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator());
        Protobuf.OrchestratorResponse response = Protobuf.OrchestratorResponse.Parser.ParseFrom(Convert.FromBase64String(responseString));
        Assert.Single(response.Actions);
        Assert.NotNull(response.Actions[0].CompleteOrchestration);
        Assert.Equal(Protobuf.OrchestrationStatus.Completed, response.Actions[0].CompleteOrchestration.OrchestrationStatus);

        // Now try it again without any properties specified. The request should still be successful.
        orchestratorRequest.Properties.Clear();
        requestBytes = orchestratorRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        responseString = GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator());
        response = Protobuf.OrchestratorResponse.Parser.ParseFrom(Convert.FromBase64String(responseString));
        Assert.Single(response.Actions);
        Assert.NotNull(response.Actions[0].CompleteOrchestration);
        Assert.Equal(Protobuf.OrchestrationStatus.Completed, response.Actions[0].CompleteOrchestration.OrchestrationStatus);
    }

    // TaskOrchestrationShim and TaskOrchestrationContextWrapper are internal to the Worker.Core assembly
    // and not visible to this test assembly via InternalsVisibleTo, so reflection is used to reach into
    // the cached shim (exposed only as the public ExtendedSessionState.TaskOrchestration property, typed
    // as the public base class TaskOrchestration) and pull out its wrapper's cached SHA1 instance.
    static SHA1 GetCachedHashAlgorithm(object extendedSessionState)
    {
        PropertyInfo taskOrchestrationProperty = extendedSessionState.GetType()
            .GetProperty("TaskOrchestration", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("ExtendedSessionState.TaskOrchestration was not found.");
        object shim = taskOrchestrationProperty.GetValue(extendedSessionState)
            ?? throw new InvalidOperationException("ExtendedSessionState.TaskOrchestration was null.");

        FieldInfo wrapperContextField = shim.GetType()
            .GetField("wrapperContext", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("TaskOrchestrationShim.wrapperContext was not found.");
        object wrapperContext = wrapperContextField.GetValue(shim)
            ?? throw new InvalidOperationException("TaskOrchestrationShim.wrapperContext was null.");

        FieldInfo cachedHashAlgorithmField = wrapperContext.GetType()
            .GetField("cachedHashAlgorithm", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "TaskOrchestrationContextWrapper.cachedHashAlgorithm was not found.");
        return (SHA1)(cachedHashAlgorithmField.GetValue(wrapperContext)
            ?? throw new InvalidOperationException("cachedHashAlgorithm was null; NewGuid() may not have run."));
    }

    static Protobuf.OrchestratorRequest CreateOrchestratorRequest(IEnumerable<Protobuf.HistoryEvent> newEvents)
    {
        var orchestratorRequest = new Protobuf.OrchestratorRequest()
        {
            InstanceId = TestInstanceId,
            PastEvents = { Enumerable.Empty<Protobuf.HistoryEvent>() },
            NewEvents = { newEvents },
            EntityParameters = new Protobuf.OrchestratorEntityParameters
            {
                EntityMessageReorderWindow = Duration.FromTimeSpan(TimeSpan.Zero),
            },
        };
        return orchestratorRequest;
    }

    class SimpleOrchestrator : TaskOrchestrator<string, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
        {
            return Task.FromResult(input);
        }
    }

    class CallSubOrchestrationOrchestrator : TaskOrchestrator<string, string>
    {
        public override async Task<string> RunAsync(TaskOrchestrationContext context, string input)
        {
            await context.CallSubOrchestratorAsync(nameof(SimpleOrchestrator));
            return input;
        }
    }

    // Same shape as CallSubOrchestrationOrchestrator (so the orchestration is left pending in the
    // extended-session cache) but also calls NewGuid() before awaiting, so the cached shim's wrapper
    // has a live SHA1 instance whose disposal we can observe once the extended session is evicted.
    class NewGuidThenCallSubOrchestrationOrchestrator : TaskOrchestrator<string, string>
    {
        public override async Task<string> RunAsync(TaskOrchestrationContext context, string input)
        {
            context.NewGuid();
            await context.CallSubOrchestratorAsync(nameof(SimpleOrchestrator));
            return input;
        }
    }
}
