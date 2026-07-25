// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
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
    public async Task ExternallyEndedExtendedSession_Evicted_DisposesCachedShimResources()
    {
        // Regression test for the round-3 lifecycle fix: the extended-session MemoryCache must dispose
        // the cached shim (and, transitively, its wrapper's cached SHA1 backing NewGuid()) whenever an
        // entry is evicted -- not just when the shim is replaced within a single Execute() call.
        //
        // Note: MemoryCache invokes post-eviction callbacks via Task.Factory.StartNew (i.e.
        // asynchronously, on a background thread), so disposal is not guaranteed to have happened by
        // the time Remove()/TryGetValue() returns. WaitUntilDisposedAsync polls with a bounded timeout
        // instead of asserting disposal immediately, to avoid flakiness under load.
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
        // entry and queues the eviction callback that disposes the cached shim. The callback runs
        // asynchronously (see the note above), which is why this test awaits WaitUntilDisposedAsync
        // below instead of asserting disposal immediately.
        orchestratorRequest.Properties.Clear();
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludeState", Value.ForBool(true) },
            { "IsExtendedSession", Value.ForBool(false) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        requestBytes = orchestratorRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        GrpcOrchestrationRunner.LoadAndRun(requestString, new NewGuidThenCallSubOrchestrationOrchestrator(), extendedSessions);
        Assert.False(extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds).TryGetValue(TestInstanceId, out _));

        await WaitUntilDisposedAsync(cachedHashAlgorithm, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Stale_ExtendedSession_Evicted_DisposesCachedShimResources_Async()
    {
        // Regression test for the round-3 lifecycle fix: sliding-expiration eviction of a stale
        // extended session must also dispose the cached shim's resources.
        //
        // Note: MemoryCache invokes post-eviction callbacks via Task.Factory.StartNew (i.e.
        // asynchronously, on a background thread), so disposal is not guaranteed to have happened
        // immediately after the scan removes the entry. WaitUntilDisposedAsync polls with a bounded
        // timeout instead of asserting disposal immediately, to avoid flakiness under load.
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

        await WaitUntilDisposedAsync(cachedHashAlgorithm, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ExtendedSessionsCache_Dispose_DisposesCachedShimResources()
    {
        // Regression test for round-4: MemoryCache.Dispose() alone does NOT invoke post-eviction
        // callbacks for entries that are still cached (confirmed against the pinned
        // Microsoft.Extensions.Caching.Memory 8.0.1 source), so ExtendedSessionsCache.Dispose() must
        // explicitly Clear() the cache before disposing it. Otherwise a still-pending extended session
        // at worker shutdown would leak its cached shim's resources (e.g. the SHA1 instance backing
        // NewGuid()) for the remaining lifetime of the process.
        var extendedSessions = new ExtendedSessionsCache();
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

        // Simulate a worker shutting down while an extended session is still pending in the cache.
        extendedSessions.Dispose();

        await WaitUntilDisposedAsync(cachedHashAlgorithm, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ExtendedSessionsCache_Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        // Regression test: ExtendedSessionsCache.Dispose() calls MemoryCache.Clear() before
        // MemoryCache.Dispose() (see above). MemoryCache.Clear() throws ObjectDisposedException if
        // the cache was already disposed, so without an idempotency guard, a second Dispose() call
        // (e.g. from a duplicate shutdown-hook invocation) would throw instead of being a safe no-op.
        var extendedSessions = new ExtendedSessionsCache();
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

        // Act: dispose the cache twice in a row.
        Exception? firstDisposeException = Record.Exception(() => extendedSessions.Dispose());
        Exception? secondDisposeException = Record.Exception(() => extendedSessions.Dispose());

        // Assert: neither call throws, and the cached shim resources are still disposed exactly once.
        Assert.Null(firstDisposeException);
        Assert.Null(secondDisposeException);
        await WaitUntilDisposedAsync(cachedHashAlgorithm, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ExtendedSessionsCache_Dispose_CalledConcurrently_DoesNotThrow()
    {
        // Regression test: guards the Dispose() idempotency fix above against a race between two
        // threads calling Dispose() at (approximately) the same time -- e.g. overlapping shutdown
        // paths -- rather than only the simpler sequential double-dispose case above.
        var extendedSessions = new ExtendedSessionsCache();
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

        // Act: dispose the cache concurrently from several threads.
        Task[] disposeTasks = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => extendedSessions.Dispose()))
            .ToArray();
        Exception? concurrentDisposeException = await Record.ExceptionAsync(() => Task.WhenAll(disposeTasks));

        // Assert: none of the concurrent calls throw, and the cached shim resources are still
        // disposed exactly once.
        Assert.Null(concurrentDisposeException);
        await WaitUntilDisposedAsync(cachedHashAlgorithm, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void GetOrInitializeCache_AfterDisposeWithoutPriorInitialization_ThrowsObjectDisposedException()
    {
        // Deterministic (non-racy) regression test for the exact bug scenario that motivated the
        // shared-lock fix: Dispose() runs while the cache has never been lazily initialized (the
        // `extendedSessions` field is still null). Without the fix, Dispose() would simply mark
        // itself disposed and return, and a *subsequent* GetOrInitializeCache() call would happily
        // construct a brand-new MemoryCache that nothing would ever dispose again (since `disposed`
        // is now permanently true). GetOrInitializeCache() must instead throw immediately.
        var extendedSessions = new ExtendedSessionsCache();

        extendedSessions.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds));
    }

    [Fact]
    public void GetOrInitializeCache_AfterDisposeOfInitializedCache_ThrowsObjectDisposedException()
    {
        // Deterministic (non-racy) regression test for the same post-dispose contract, but covering
        // the case where the cache *was* already lazily initialized (and thus disposed/torn down by
        // Dispose()) before the later GetOrInitializeCache() call is made.
        var extendedSessions = new ExtendedSessionsCache();
        extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds);

        extendedSessions.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds));
    }

    [Fact]
    public async Task ExtendedSessionsCache_DisposeRaceWithGetOrInitializeCache_NeverLeaksCache()
    {
        // Regression test: guards against a race between Dispose() and GetOrInitializeCache() where
        // Dispose() could observe the `extendedSessions` field as still null (not yet lazily
        // created), mark itself disposed, and return having done nothing -- while a concurrent
        // GetOrInitializeCache() call then constructs a brand-new MemoryCache that Dispose() has
        // already finished running and will never see again, permanently leaking it (all future
        // Dispose() calls short-circuit once `disposed` is true).
        //
        // The fix synchronizes both methods under a single shared lock, so the two operations are
        // always fully serialized -- never interleaved -- for any given ExtendedSessionsCache
        // instance:
        //  * If GetOrInitializeCache() completes (and returns a cache) strictly before Dispose()
        //    acquires the lock, Dispose() is then guaranteed to observe and dispose that exact
        //    cache.
        //  * If Dispose() completes strictly first, GetOrInitializeCache() must throw
        //    ObjectDisposedException instead of creating a now-unreachable cache.
        //
        // A Barrier coordinates the two competing threads to start racing at (approximately) the
        // same instant on every iteration -- without it, Task.Run scheduling order alone tends to
        // let whichever task was queued first win essentially every time. This is used only to
        // encourage genuine contention on the shared lock; it does not (and cannot) guarantee that
        // both the "GetOrInitializeCache() wins" and "Dispose() wins" orderings occur across the
        // iterations below -- a Barrier release is not a scheduling guarantee, and correct,
        // race-free code may legitimately let the same side win every single iteration depending on
        // thread-pool scheduling. Asserting that both orderings must occur would therefore make this
        // test's pass/fail outcome probabilistic (and CI-flaky) rather than a genuine correctness
        // check. Instead, this test asserts only outcomes that must hold under *every* possible
        // interleaving: whichever side wins, no cache is ever leaked or double-disposed.
        //
        // Each SignalAndWait() call uses a bounded timeout rather than waiting indefinitely, so a
        // hung/stalled participant surfaces as a test failure (via TimeoutException) instead of the
        // test run hanging.
        //
        // Exact-once disposal of cached *content* tied to eviction is verified separately and
        // deterministically by ExtendedSessionsCache_Dispose_DisposesCachedEntryExactlyOnce below --
        // racing an entry Set() call concurrently against this same Dispose() would itself introduce
        // a spurious window (between Dispose()'s internal Clear() and its subsequent Dispose() call)
        // where an entry added in between would never be evicted-and-disposed by *this* cache
        // instance, which is a test-harness artifact rather than anything a real caller does.
        TimeSpan barrierTimeout = TimeSpan.FromSeconds(10);

        for (int iteration = 0; iteration < 50; iteration++)
        {
            var extendedSessions = new ExtendedSessionsCache();
            using var barrier = new Barrier(2);

            Task<MemoryCache?> getOrInitTask = Task.Run(() =>
            {
                if (!barrier.SignalAndWait(barrierTimeout))
                {
                    throw new TimeoutException(
                        "Barrier synchronization timed out waiting for both racing tasks to start.");
                }

                try
                {
                    return extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds);
                }
                catch (ObjectDisposedException)
                {
                    // Losing the race to a Dispose() that ran first is an expected, safe outcome.
                    return null;
                }
            });
            Task disposeTask = Task.Run(() =>
            {
                if (!barrier.SignalAndWait(barrierTimeout))
                {
                    throw new TimeoutException(
                        "Barrier synchronization timed out waiting for both racing tasks to start.");
                }

                extendedSessions.Dispose();
            });

            await Task.WhenAll(getOrInitTask, disposeTask);
            MemoryCache? cache = await getOrInitTask;

            if (cache is not null)
            {
                // GetOrInitializeCache() won the race and returned a cache. Because both methods are
                // mutually exclusive under the shared lock, and Task.WhenAll has already awaited the
                // Dispose() call to completion, Dispose() must have run strictly after
                // initialization -- so it is guaranteed to have already captured and disposed this
                // exact cache instance. Verify it is genuinely disposed (not merely leaked out of
                // reach) by asserting further use throws ObjectDisposedException.
                Assert.Throws<ObjectDisposedException>(() => cache.TryGetValue("any-key", out _));
            }

            // Regardless of which call won the race, a repeated Dispose() call must remain a safe,
            // idempotent no-op -- proving the cache reached a single, well-defined disposed state
            // with no lingering, undisposed MemoryCache left behind.
            Exception? repeatDisposeException = Record.Exception(() => extendedSessions.Dispose());
            Assert.Null(repeatDisposeException);
        }
    }

    [Fact]
    public async Task ExtendedSessionsCache_Dispose_DisposesCachedEntryExactlyOnce()
    {
        // Deterministic (non-racing) regression test proving that Dispose() drives exact-once
        // disposal of *cached content* via the eviction-callback path, not merely that the owning
        // MemoryCache object itself becomes unusable afterwards. A CountingDisposable spy is
        // registered with a post-eviction callback wired up exactly like GrpcOrchestrationRunner
        // does for real cached shims, so the assertion reflects genuine production disposal wiring.
        var extendedSessions = new ExtendedSessionsCache();
        MemoryCache cache = extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds);

        var spy = new CountingDisposable();
        var options = new MemoryCacheEntryOptions();
        options.RegisterPostEvictionCallback(
            (key, value, reason, state) => ((CountingDisposable)value!).Dispose());
        cache.Set("spy", spy, options);

        Assert.Equal(0, spy.DisposeCount);

        extendedSessions.Dispose();

        await WaitUntilDisposedAsync(spy, TimeSpan.FromSeconds(10));
        Assert.Equal(1, spy.DisposeCount);

        // A repeated Dispose() call must not trigger a second eviction/disposal of the same entry.
        extendedSessions.Dispose();
        Assert.Equal(1, spy.DisposeCount);
    }

    [Fact]
    public void LoadAndRun_ExtendedSession_CacheDisposedDuringExecution_ShimIsDisposedImmediatelyNotLeaked()
    {
        // Regression test for the round-9 shutdown/hand-off race, exercised through the full
        // GrpcOrchestrationRunner.LoadAndRun pipeline. The orchestrator disposes the extended-sessions
        // cache itself partway through its own execution -- simulating a graceful
        // worker shutdown completing while this orchestration is still in flight, holding a MemoryCache
        // reference that GrpcOrchestrationRunner obtained before the shutdown began. Because the
        // orchestration does not complete on this execution (it awaits a sub-orchestration call),
        // GrpcOrchestrationRunner attempts to hand its shim off to the cache afterward; with the round-9
        // fix, that hand-off is rejected (the cache is disposed), so the shim's wrapper is disposed
        // immediately and synchronously in the `finally` block, rather than being silently leaked in a
        // cache that will never evict or dispose it again.
        var extendedSessions = new ExtendedSessionsCache();

        // Obtain the cache reference before "shutdown" -- exactly as GrpcOrchestrationRunner does at the
        // very start of LoadAndRun, well before the orchestrator's Dispose() call (below) runs.
        extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds);

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

        var orchestrator = new DisposeCacheDuringExecutionOrchestrator(extendedSessions);
        string responseString = GrpcOrchestrationRunner.LoadAndRun(requestString, orchestrator, extendedSessions);

        Assert.NotNull(orchestrator.CapturedHashAlgorithm);
        Assert.Throws<ObjectDisposedException>(() => orchestrator.CapturedHashAlgorithm!.ComputeHash([1, 2, 3]));

        Protobuf.OrchestratorResponse response = Protobuf.OrchestratorResponse.Parser.ParseFrom(Convert.FromBase64String(responseString));
        Assert.False(response.RequiresHistory);
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

    // Like GetCachedHashAlgorithm above, but reaches directly into the TaskOrchestrationContext
    // instance passed to an orchestrator's RunAsync -- which, per TaskOrchestrationShim, is exactly
    // the shim's wrapperContext instance -- instead of going through a cached ExtendedSessionState.
    // Used by DisposeCacheDuringExecutionOrchestrator, whose cache hand-off is rejected (round-9 fix),
    // so its shim is never cached and thus unreachable via ExtendedSessionState afterward.
    static SHA1 GetCachedHashAlgorithmFromContext(TaskOrchestrationContext context)
    {
        FieldInfo cachedHashAlgorithmField = context.GetType()
            .GetField("cachedHashAlgorithm", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "TaskOrchestrationContextWrapper.cachedHashAlgorithm was not found.");
        return (SHA1)(cachedHashAlgorithmField.GetValue(context)
            ?? throw new InvalidOperationException("cachedHashAlgorithm was null; NewGuid() may not have run."));
    }

    // Eviction callbacks on the extended-sessions MemoryCache are dispatched via
    // Task.Factory.StartNew (i.e. asynchronously, on a background thread pool task) rather than
    // synchronously on the calling thread -- see Microsoft.Extensions.Caching.Memory's
    // CacheEntryTokens.InvokeEvictionCallbacks. This helper polls with a bounded timeout for the given
    // SHA1 instance to become disposed, instead of asserting disposal immediately after triggering an
    // eviction, to avoid flakiness from that inherent async scheduling.
    static async Task WaitUntilDisposedAsync(SHA1 hashAlgorithm, TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                hashAlgorithm.ComputeHash([1, 2, 3]);
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (stopwatch.Elapsed >= timeout)
            {
                throw new TimeoutException(
                    $"SHA1 instance was not disposed within {timeout} of the triggering eviction/disposal.");
            }

            await Task.Delay(20);
        }
    }

    // Same bounded-polling shape as the SHA1 overload above, but for the CountingDisposable spy used
    // by the Dispose()/GetOrInitializeCache() race test, since MemoryCache eviction callbacks (and
    // thus disposal of a cache entry's content) are likewise dispatched asynchronously.
    static async Task WaitUntilDisposedAsync(CountingDisposable spy, TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (spy.DisposeCount == 0)
        {
            if (stopwatch.Elapsed >= timeout)
            {
                throw new TimeoutException(
                    $"CountingDisposable spy was not disposed within {timeout} of the triggering eviction/disposal.");
            }

            await Task.Delay(20);
        }
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

    // Regression orchestrator for the round-9 shutdown/hand-off race: disposes the extended-sessions
    // cache passed to its constructor partway through its own execution -- after calling NewGuid() so
    // there is a live cached SHA1 to observe -- simulating a graceful worker shutdown completing while
    // this orchestration is still in flight and holds a MemoryCache reference obtained before the
    // shutdown began. It then awaits a sub-orchestration call (like CallSubOrchestrationOrchestrator)
    // so the orchestration does not complete on this execution, forcing GrpcOrchestrationRunner to
    // attempt a hand-off of its shim to the now-disposed cache afterward.
    class DisposeCacheDuringExecutionOrchestrator : TaskOrchestrator<string, string>
    {
        readonly ExtendedSessionsCache cacheToDisposeDuringExecution;

        public DisposeCacheDuringExecutionOrchestrator(ExtendedSessionsCache cacheToDisposeDuringExecution)
        {
            this.cacheToDisposeDuringExecution = cacheToDisposeDuringExecution;
        }

        public SHA1? CapturedHashAlgorithm { get; private set; }

        public override async Task<string> RunAsync(TaskOrchestrationContext context, string input)
        {
            context.NewGuid();
            this.CapturedHashAlgorithm = GetCachedHashAlgorithmFromContext(context);

            this.cacheToDisposeDuringExecution.Dispose();

            await context.CallSubOrchestratorAsync(nameof(SimpleOrchestrator));
            return input;
        }
    }

    // Minimal disposable spy used by the Dispose()/GetOrInitializeCache() race test to verify
    // exact-once disposal semantics precisely -- via a real, observable Dispose() call count -- rather
    // than only inferring disposal indirectly through the owning MemoryCache object becoming unusable.
    sealed class CountingDisposable : IDisposable
    {
        int disposeCount;

        public int DisposeCount => Volatile.Read(ref this.disposeCount);

        public void Dispose() => Interlocked.Increment(ref this.disposeCount);
    }
}
