// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Microsoft.DurableTask.Worker.Grpc;

namespace Microsoft.DurableTask.Worker.Tests;
public class GrpcOrchestrationRunnerTests
{
    const string TestInstanceId = "instance_id";
    const string TestExecutionId = "execution_id";
    const int DefaultExtendedSessionIdleTimeoutInSeconds = 300;

    [Fact]
    public void EmptyOrNullParameters_Throw()
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

        act = () =>
            GrpcOrchestrationRunner.LoadAndRun("request", new SimpleOrchestrator(), extendedSessionsCache: null!);
        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("extendedSessionsCache");
    }

    [Fact]
    public void EmptyHistory_Returns_NeedsHistoryInResponse()
    {
        using var extendedSessions = new ExtendedSessionsCache();

        // No history and without extended sessions enabled
        var orchestratorRequest = CreateOrchestratorRequest([]);
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludePastEvents", Value.ForBool(false) },
            { "ExtendedSession", Value.ForBool(false) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        byte[] requestBytes = orchestratorRequest.ToByteArray();
        string requestString =  Convert.ToBase64String(requestBytes);
        string stringResponse = GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator(), extendedSessions);
        Protobuf.OrchestratorResponse response = Protobuf.OrchestratorResponse.Parser.ParseFrom(Convert.FromBase64String(stringResponse));
        Assert.True(response.RequiresHistory);
        Assert.False(extendedSessions.IsInitialized());

        // No history but with extended sessions enabled
        orchestratorRequest.Properties.Clear();
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "ExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        requestBytes = orchestratorRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        stringResponse = GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator(), extendedSessions);
        response = Protobuf.OrchestratorResponse.Parser.ParseFrom(Convert.FromBase64String(stringResponse));
        Assert.True(response.RequiresHistory);
        Assert.True(extendedSessions.IsInitialized());
    }

    [Fact]
    public void MalformedRequestParameters_Means_NoExtendedSessionsStored()
    {
        using var extendedSessions = new ExtendedSessionsCache();
        var orchestratorRequest = CreateOrchestratorRequest([]);

        // Misspelled extended session timeout key
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludePastEvents", Value.ForBool(false) },
            { "ExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionsIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        byte[] requestBytes = orchestratorRequest.ToByteArray();
        string requestString = Convert.ToBase64String(requestBytes);
        string stringResponse = GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator(), extendedSessions);
        Protobuf.OrchestratorResponse response = Protobuf.OrchestratorResponse.Parser.ParseFrom(Convert.FromBase64String(stringResponse));
        Assert.False(extendedSessions.IsInitialized());

        // Wrong value type for extended session timeout key
        orchestratorRequest.Properties.Clear();
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludePastEvents", Value.ForBool(false) },
            { "ExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForString("hi") } });
        requestBytes = orchestratorRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator(), extendedSessions);
        Assert.False(extendedSessions.IsInitialized());

        // No extended session timeout key
        orchestratorRequest.Properties.Clear();
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludePastEvents", Value.ForBool(false) },
            { "ExtendedSession", Value.ForBool(true) } });
        requestBytes = orchestratorRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator(), extendedSessions);
        Assert.False(extendedSessions.IsInitialized());

        // Misspelled extended session key
        orchestratorRequest.Properties.Clear();
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludePastEvents", Value.ForBool(false) },
            { "extendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        requestBytes = orchestratorRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        stringResponse = GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator(), extendedSessions);
        response = Protobuf.OrchestratorResponse.Parser.ParseFrom(Convert.FromBase64String(stringResponse));
        // The extended session is still initialized due to the well-formed extended session timeout key
        Assert.True(extendedSessions.IsInitialized());
        Assert.False(extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds).TryGetValue(TestInstanceId, out object? extendedSession));

        // Wrong value type for extended session key
        orchestratorRequest.Properties.Clear();
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludePastEvents", Value.ForBool(false) },
            { "ExtendedSession", Value.ForNumber(1) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        requestBytes = orchestratorRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        stringResponse = GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator(), extendedSessions);
        response = Protobuf.OrchestratorResponse.Parser.ParseFrom(Convert.FromBase64String(stringResponse));
        // The extended session is still initialized due to the well-formed extended session timeout key
        Assert.True(extendedSessions.IsInitialized());
        Assert.False(extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds).TryGetValue(TestInstanceId, out extendedSession));

        // No extended session key
        orchestratorRequest.Properties.Clear();
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludePastEvents", Value.ForBool(false) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        requestBytes = orchestratorRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        stringResponse = GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator(), extendedSessions);
        response = Protobuf.OrchestratorResponse.Parser.ParseFrom(Convert.FromBase64String(stringResponse));
        // The extended session is still initialized due to the well-formed extended session timeout key
        Assert.True(extendedSessions.IsInitialized());
        Assert.False(extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds).TryGetValue(TestInstanceId, out extendedSession));
    }

    /// <summary>
    /// These tests verify that a malformed/nonexistent "IncludePastEvents" parameter means that the worker will attempt to 
    /// fulfill the orchestration request and not request a history for it. However, it is of course very undesirable that a 
    /// history is not attached to the request, but no history is requested by the worker due to a malformed "IncludePastEvents" parameter
    /// even when it needs one to fulfill the request. This would need to be checked on whatever side is calling this SDK. 
    /// </summary>
    [Fact]
    public void MalformedPastEventsParameter_Means_NoHistoryRequired()
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
            { "INcludePastEvents", Value.ForBool(false) },
            { "ExtendedSession", Value.ForBool(false) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        byte[] requestBytes = orchestratorRequest.ToByteArray();
        string requestString = Convert.ToBase64String(requestBytes);
        string stringResponse = GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator(), extendedSessions);
        Protobuf.OrchestratorResponse response = Protobuf.OrchestratorResponse.Parser.ParseFrom(Convert.FromBase64String(stringResponse));
        Assert.False(response.RequiresHistory);

        // Wrong value type for include past events key
        orchestratorRequest.Properties.Clear();
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludePastEvents", Value.ForString("no") },
            { "ExtendedSession", Value.ForBool(false) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        requestBytes = orchestratorRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        stringResponse = GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator(), extendedSessions);
        response = Protobuf.OrchestratorResponse.Parser.ParseFrom(Convert.FromBase64String(stringResponse));
        Assert.False(response.RequiresHistory);

        // No include past events key
        orchestratorRequest.Properties.Clear();
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "ExtendedSession", Value.ForBool(false) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        requestBytes = orchestratorRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        stringResponse = GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator(), extendedSessions);
        response = Protobuf.OrchestratorResponse.Parser.ParseFrom(Convert.FromBase64String(stringResponse));
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
            { "IncludePastEvents", Value.ForBool(true) },
            { "ExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        byte[] requestBytes = orchestratorRequest.ToByteArray();
        string requestString = Convert.ToBase64String(requestBytes);
        GrpcOrchestrationRunner.LoadAndRun(requestString, new CallSubOrchestrationOrchestrator(), extendedSessions);
        Assert.True(extendedSessions.IsInitialized());
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
            { "IncludePastEvents", Value.ForBool(true) },
            { "ExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        byte[] requestBytes = orchestratorRequest.ToByteArray();
        string requestString = Convert.ToBase64String(requestBytes);
        GrpcOrchestrationRunner.LoadAndRun(requestString, new SimpleOrchestrator(), extendedSessions);
        Assert.True(extendedSessions.IsInitialized());
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
            { "IncludePastEvents", Value.ForBool(true) },
            { "ExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        byte[] requestBytes = orchestratorRequest.ToByteArray();
        string requestString = Convert.ToBase64String(requestBytes);
        GrpcOrchestrationRunner.LoadAndRun(requestString, new CallSubOrchestrationOrchestrator(), extendedSessions);
        Assert.True(extendedSessions.IsInitialized());
        Assert.True(extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds).TryGetValue(TestInstanceId, out object? extendedSession));

        // Now set the extended session flag to false for this instance
        orchestratorRequest.Properties.Clear();
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludePastEvents", Value.ForBool(true) },
            { "ExtendedSession", Value.ForBool(false) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(DefaultExtendedSessionIdleTimeoutInSeconds) } });
        requestBytes = orchestratorRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        GrpcOrchestrationRunner.LoadAndRun(requestString, new CallSubOrchestrationOrchestrator(), extendedSessions);
        Assert.True(extendedSessions.IsInitialized());
        Assert.False(extendedSessions.GetOrInitializeCache(DefaultExtendedSessionIdleTimeoutInSeconds).TryGetValue(TestInstanceId, out extendedSession));
    }

    [Fact]
    public async void Stale_ExtendedSessions_Evicted_Async()
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
            { "IncludePastEvents", Value.ForBool(true) },
            { "ExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(extendedSessionIdleTimeout) } });
        byte[] requestBytes = orchestratorRequest.ToByteArray();
        string requestString = Convert.ToBase64String(requestBytes);
        GrpcOrchestrationRunner.LoadAndRun(requestString, new CallSubOrchestrationOrchestrator(), extendedSessions);
        Assert.True(extendedSessions.IsInitialized());
        Assert.True(extendedSessions.GetOrInitializeCache(extendedSessionIdleTimeout).TryGetValue(TestInstanceId, out object? extendedSession));

        // Wait for longer than the timeout to account for finite cache scan for stale items frequency 
        await Task.Delay(extendedSessionIdleTimeout * 1000 * 2);
        Assert.False(extendedSessions.GetOrInitializeCache(extendedSessionIdleTimeout).TryGetValue(TestInstanceId, out extendedSession));

        // Now that the entry was evicted from the cache, the orchestration runner needs an orchestration history to complete the request
        orchestratorRequest.Properties.Clear();
        orchestratorRequest.Properties.Add(new MapField<string, Value>() {
            { "IncludePastEvents", Value.ForBool(false) },
            { "ExtendedSession", Value.ForBool(true) },
            { "ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(extendedSessionIdleTimeout) } });
        requestBytes = orchestratorRequest.ToByteArray();
        requestString = Convert.ToBase64String(requestBytes);
        string stringResponse = GrpcOrchestrationRunner.LoadAndRun(requestString, new CallSubOrchestrationOrchestrator(), extendedSessions);
        Protobuf.OrchestratorResponse response = Protobuf.OrchestratorResponse.Parser.ParseFrom(Convert.FromBase64String(stringResponse));
        Assert.True(response.RequiresHistory);
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
