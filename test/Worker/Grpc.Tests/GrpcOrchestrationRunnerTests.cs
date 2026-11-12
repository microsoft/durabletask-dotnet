// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Microsoft.DurableTask.Converters;
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

    [Theory]
    [InlineData(null, false)]
    [InlineData("original-instance", false)]
    [InlineData("previous-clone", false)]
    [InlineData("dt:2026-01-01T00:00:00.0000000Z", false)]
    [InlineData("dto:2026-01-01T00:00:00.0000000+00:00", false)]
    [InlineData(null, true)]
    [InlineData("original-instance", true)]
    [InlineData("previous-clone", true)]
    [InlineData("dt:2026-01-01T00:00:00.0000000Z", true)]
    [InlineData("dto:2026-01-01T00:00:00.0000000+00:00", true)]
    public void SourceInstanceId_FlowsThroughPropertiesOnExecutionAndReplay(string? source, bool replay)
    {
        // Arrange
        Protobuf.HistoryEvent started = new()
        {
            EventId = -1,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ExecutionStarted = new()
            {
                Name = "CloneOrchestration",
                Input = "\"original-input\"",
                OrchestrationInstance = new()
                {
                    InstanceId = TestInstanceId,
                    ExecutionId = TestExecutionId,
                },
            },
        };
        Protobuf.OrchestratorRequest request = CreateOrchestratorRequest(replay ? [] : [started]);
        if (replay)
        {
            request.PastEvents.Add(started);
        }

        if (source is not null)
        {
            request.Properties.Add("sourceInstanceId", Value.ForString(source));
        }

        request.Properties.Add("dateTime", Value.ForString("dt:2026-01-01T00:00:00.0000000Z"));
        request.Properties.Add("dateTimeOffset", Value.ForString("dto:2026-01-01T00:00:00.0000000+00:00"));
        string? actualSource = null;
        bool? actualReplay = null;
        string? actualInput = null;
        object? actualDateTime = null;
        object? actualDateTimeOffset = null;
        ITaskOrchestrator orchestrator = FuncTaskOrchestrator.Create<string, string?>((context, _) =>
        {
            actualSource = context.SourceInstanceId;
            actualReplay = context.IsReplaying;
            actualInput = context.GetInput<string>();
            actualDateTime = context.Properties["dateTime"];
            actualDateTimeOffset = context.Properties["dateTimeOffset"];
            return Task.FromResult(context.SourceInstanceId);
        });

        // Act
        string encodedResponse = GrpcOrchestrationRunner.LoadAndRun(
            Convert.ToBase64String(request.ToByteArray()), orchestrator);
        Protobuf.OrchestratorResponse response = Protobuf.OrchestratorResponse.Parser.ParseFrom(
            Convert.FromBase64String(encodedResponse));

        // Assert
        actualSource.Should().Be(source);
        actualReplay.Should().Be(replay);
        actualInput.Should().Be("original-input");
        actualDateTime.Should().BeOfType<DateTime>().Which.Should().Be(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        actualDateTimeOffset.Should().BeOfType<DateTimeOffset>().Which.Should().Be(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        response.Actions.Should().ContainSingle();
        response.Actions[0].CompleteOrchestration.OrchestrationStatus.Should().Be(Protobuf.OrchestrationStatus.Completed);
        JsonDataConverter.Default.Deserialize<string>(response.Actions[0].CompleteOrchestration.Result).Should().Be(source);
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
}
