// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DurableTask.Core;
using DurableTask.Core.History;
using Microsoft.DurableTask.Grpc;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using P = Microsoft.DurableTask.Protobuf;
using Microsoft.DurableTask.Options;

namespace Microsoft.DurableTask;

public static class OrchestrationRunner
{
    /// <summary>
    /// Deserializes orchestration history from <paramref name="encodedOrchestratorRequest"/> and uses it to execute the orchestrator function
    /// code pointed to by <paramref name="orchestratorFunc"/>.
    /// </summary>
    /// <typeparam name="TInput">The type of the orchestrator function input. This type must be deserializeable from JSON.</typeparam>
    /// <typeparam name="TOutput">The type of the orchestrator function output. This type must be serializeable to JSON.</typeparam>
    /// <param name="encodedOrchestratorRequest">The encoded protobuf payload representing an orchestration execution request. This is a base64-encoded string.</param>
    /// <param name="orchestratorFunc">A function that implements the orchestrator logic.</param>
    /// <param name="services">Optional <see cref="IServiceProvider"/> from which injected dependencies can be retrieved.</param>
    /// <returns>Returns a serialized set of orchestrator actions that should be used as the return value of the orchestrator function trigger.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="encodedOrchestratorRequest"/> or <paramref name="orchestratorFunc"/> is <c>null</c>.</exception>
    public static string LoadAndRun<TInput, TOutput>(
        string encodedOrchestratorRequest,
        Func<TaskOrchestrationContext, TInput?, Task<TOutput?>> orchestratorFunc,
        IServiceProvider? services = null)
    {
        if (orchestratorFunc == null)
        {
            throw new ArgumentNullException(nameof(orchestratorFunc));
        }

        FuncTaskOrchestrator<TInput, TOutput> orchestrator = new(orchestratorFunc);
        return LoadAndRun(encodedOrchestratorRequest, orchestrator, services);
    }

    /// <summary>
    /// Deserializes orchestration history from <paramref name="encodedOrchestratorRequest"/> and uses it to resume the orchestrator
    /// implemented by <paramref name="implementation"/>.
    /// </summary>
    /// <param name="encodedOrchestratorRequest">The encoded protobuf payload representing an orchestration execution request. This is a base64-encoded string.</param>
    /// <param name="implementation">An <see cref="ITaskOrchestrator"/> implementation that defines the orchestrator logic.</param>
    /// <param name="services">Optional <see cref="IServiceProvider"/> from which injected dependencies can be retrieved.</param>
    /// <returns>Returns a serialized set of orchestrator actions that should be used as the return value of the orchestrator function trigger.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="encodedOrchestratorRequest"/> or <paramref name="implementation"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="encodedOrchestratorRequest"/> contains invalid data.</exception>
    public static string LoadAndRun(
        string encodedOrchestratorRequest,
        ITaskOrchestrator implementation,
        IServiceProvider? services = null)
    {
        if (string.IsNullOrEmpty(encodedOrchestratorRequest))
        {
            throw new ArgumentNullException(nameof(encodedOrchestratorRequest));
        }

        if (implementation == null)
        {
            throw new ArgumentNullException(nameof(implementation));
        }

        P.OrchestratorRequest request = ProtoUtils.Base64Decode<P.OrchestratorRequest>(
            encodedOrchestratorRequest,
            P.OrchestratorRequest.Parser);

        List<HistoryEvent> pastEvents = request.PastEvents.Select(ProtoUtils.ConvertHistoryEvent).ToList();
        IEnumerable<HistoryEvent> newEvents = request.NewEvents.Select(ProtoUtils.ConvertHistoryEvent);

        DataConverter dataConverter = services?.GetService<DataConverter>() ?? SdkUtils.DefaultDataConverter;
        ILoggerFactory loggerFactory = services?.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
        ILogger logger = SdkUtils.GetLogger(loggerFactory);
        TimerOptions timerOptions = TimerOptions.Default; // TODO: Support loading timer options from configuration

        WorkerContext workerContext = new(
            dataConverter,
            logger,
            services ?? SdkUtils.EmptyServiceProvider,
            timerOptions);

        // Re-construct the orchestration state from the history.
        // New events must be added using the AddEvent method.
        OrchestrationRuntimeState runtimeState = new(pastEvents);
        foreach (HistoryEvent newEvent in newEvents)
        {
            runtimeState.AddEvent(newEvent);
        }

        TaskName orchestratorName = new(runtimeState.Name, runtimeState.Version);

        TaskOrchestrationShim orchestrator = new(workerContext, orchestratorName, implementation);
        TaskOrchestrationExecutor executor = new(runtimeState, orchestrator, BehaviorOnContinueAsNew.Carryover);
        OrchestratorExecutionResult result = executor.Execute();

        P.OrchestratorResponse response = ProtoUtils.ConstructOrchestratorResponse(
            request.InstanceId,
            result.CustomStatus,
            result.Actions);
        byte[] responseBytes = response.ToByteArray();
        return Convert.ToBase64String(responseBytes);
    }
}
