// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.History;
using Google.Protobuf;
using Microsoft.DurableTask.Worker.Shims;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc;

/// <summary>
/// Helper class for invoking orchestrations directly, without building a worker instance.
/// </summary>
/// <remarks>
/// <para>
/// This static class can be used to execute orchestration logic directly. In order to use it for this purpose, the
/// caller must provide orchestration state as serialized protobuf bytes.
/// </para><para>
/// The Azure Functions .NET worker extension is the primary intended user of this class, where orchestration state
/// is provided by trigger bindings.
/// </para>
/// </remarks>
public static class GrpcOrchestrationRunner
{
    /// <summary>
    /// Loads orchestration history from <paramref name="encodedOrchestratorRequest"/> and uses it to execute the
    /// orchestrator function code pointed to by <paramref name="orchestratorFunc"/>.
    /// </summary>
    /// <typeparam name="TInput">
    /// The type of the orchestrator function input. This type must be deserializable from JSON.
    /// </typeparam>
    /// <typeparam name="TOutput">
    /// The type of the orchestrator function output. This type must be serializable to JSON.
    /// </typeparam>
    /// <param name="encodedOrchestratorRequest">
    /// The base64-encoded protobuf payload representing an orchestration execution request.
    /// </param>
    /// <param name="orchestratorFunc">A function that implements the orchestrator logic.</param>
    /// <param name="services">
    /// Optional <see cref="IServiceProvider"/> from which injected dependencies can be retrieved.
    /// </param>
    /// <returns>
    /// Returns a base64-encoded set of orchestrator actions to be interpreted by the external orchestration engine.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="encodedOrchestratorRequest"/> or <paramref name="orchestratorFunc"/> is <c>null</c>.
    /// </exception>
    public static string LoadAndRun<TInput, TOutput>(
        string encodedOrchestratorRequest,
        Func<TaskOrchestrationContext, TInput?, Task<TOutput?>> orchestratorFunc,
        IServiceProvider? services = null)
    {
        Check.NotNull(orchestratorFunc);
        return LoadAndRun(encodedOrchestratorRequest, FuncTaskOrchestrator.Create(orchestratorFunc), services);
    }

    /// <summary>
    /// Deserializes orchestration history from <paramref name="encodedOrchestratorRequest"/> and uses it to resume the
    /// orchestrator implemented by <paramref name="implementation"/>.
    /// </summary>
    /// <param name="encodedOrchestratorRequest">
    /// The encoded protobuf payload representing an orchestration execution request. This is a base64-encoded string.
    /// </param>
    /// <param name="implementation">
    /// An <see cref="ITaskOrchestrator"/> implementation that defines the orchestrator logic.
    /// </param>
    /// <param name="services">
    /// Optional <see cref="IServiceProvider"/> from which injected dependencies can be retrieved.
    /// </param>
    /// <returns>
    /// Returns a serialized set of orchestrator actions that should be used as the return value of the orchestrator function trigger.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="encodedOrchestratorRequest"/> or <paramref name="implementation"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown if <paramref name="encodedOrchestratorRequest"/> contains invalid data.
    /// </exception>
    public static string LoadAndRun(
        string encodedOrchestratorRequest,
        ITaskOrchestrator implementation,
        IServiceProvider? services = null)
    {
        return LoadAndRun(encodedOrchestratorRequest, implementation, extendedSessionsCache: null, services: services);
    }

    /// <summary>
    /// Deserializes orchestration history from <paramref name="encodedOrchestratorRequest"/> and uses it to resume the
    /// orchestrator implemented by <paramref name="implementation"/>.
    /// </summary>
    /// <param name="encodedOrchestratorRequest">
    /// The encoded protobuf payload representing an orchestration execution request. This is a base64-encoded string.
    /// </param>
    /// <param name="implementation">
    /// An <see cref="ITaskOrchestrator"/> implementation that defines the orchestrator logic.
    /// </param>
    /// <param name="extendedSessionsCache">
    /// The cache of extended sessions which can be used to retrieve the <see cref="ExtendedSessionState"/> if this orchestration request is from within an extended session.
    /// </param>
    /// <param name="services">
    /// Optional <see cref="IServiceProvider"/> from which injected dependencies can be retrieved.
    /// </param>
    /// <returns>
    /// Returns a serialized set of orchestrator actions that should be used as the return value of the orchestrator function trigger.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="encodedOrchestratorRequest"/> or <paramref name="implementation"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown if <paramref name="encodedOrchestratorRequest"/> contains invalid data.
    /// </exception>
    public static string LoadAndRun(
        string encodedOrchestratorRequest,
        ITaskOrchestrator implementation,
        ExtendedSessionsCache? extendedSessionsCache,
        IServiceProvider? services = null)
    {
        Check.NotNullOrEmpty(encodedOrchestratorRequest);
        Check.NotNull(implementation);

        P.OrchestratorRequest request = P.OrchestratorRequest.Parser.Base64Decode<P.OrchestratorRequest>(
            encodedOrchestratorRequest);

        ProtoUtils.EntityConversionState entityConversionState = new(insertMissingEntityUnlocks: false);

        Func<P.HistoryEvent, HistoryEvent> converter = entityConversionState.ConvertFromProto;

        List<HistoryEvent> pastEvents = request.PastEvents.Select(converter).ToList();
        IEnumerable<HistoryEvent> newEvents = request.NewEvents.Select(converter);
        Dictionary<string, object?> properties = request.Properties.ToDictionary(
                pair => pair.Key,
                pair => ProtoUtils.ConvertValueToObject(pair.Value));

        OrchestratorExecutionResult? result = null;

        bool addToExtendedSessions = false;
        bool requiresHistory = false;
        GrpcInstanceRunnerUtils.ParseRequestPropertiesAndInitializeCache(
            properties,
            extendedSessionsCache,
            out double extendedSessionIdleTimeoutInSeconds,
            out bool isExtendedSession,
            out bool pastEventsIncluded,
            out MemoryCache? extendedSessions);

        if (isExtendedSession && extendedSessions != null)
        {
            // If a history was provided, even if we already have an extended session stored, we always want to evict whatever state is in the cache and replace it with a new extended
            // session based on the provided history
            if (!pastEventsIncluded && extendedSessions.TryGetValue(request.InstanceId, out ExtendedSessionState? extendedSessionState) && extendedSessionState is not null)
            {
                OrchestrationRuntimeState runtimeState = extendedSessionState!.RuntimeState;
                runtimeState.NewEvents.Clear();
                foreach (HistoryEvent newEvent in newEvents)
                {
                    runtimeState.AddEvent(newEvent);
                }

                result = extendedSessionState.OrchestrationExecutor.ExecuteNewEvents();
                if (extendedSessionState.OrchestrationExecutor.IsCompleted)
                {
                    extendedSessions.Remove(request.InstanceId);
                }
            }
            else
            {
                extendedSessions.Remove(request.InstanceId);
                addToExtendedSessions = true;
            }
        }

        if (result == null)
        {
            // DurableTask.Core did not attach the orchestration history since the extended session is still active on its end, but we have since evicted the
            // session and lost the orchestration history so we cannot replay the orchestration.
            if (!pastEventsIncluded)
            {
                requiresHistory = true;
            }
            else
            {
                // Re-construct the orchestration state from the history.
                // New events must be added using the AddEvent method.
                OrchestrationRuntimeState runtimeState = new(pastEvents);

                foreach (HistoryEvent newEvent in newEvents)
                {
                    runtimeState.AddEvent(newEvent);
                }

                TaskName orchestratorName = new(runtimeState.Name);
                ParentOrchestrationInstance? parent = runtimeState.ParentInstance is ParentInstance p
                    ? new(new(p.Name), p.OrchestrationInstance.InstanceId)
                    : null;

                DurableTaskShimFactory factory = services is null
                    ? DurableTaskShimFactory.Default
                    : ActivatorUtilities.GetServiceOrCreateInstance<DurableTaskShimFactory>(services);
                TaskOrchestration shim = factory.CreateOrchestration(orchestratorName, implementation, properties, parent);

                TaskOrchestrationExecutor executor = new(
                    runtimeState,
                    shim,
                    BehaviorOnContinueAsNew.Carryover,
                    request.EntityParameters.ToCore(),
                    ErrorPropagationMode.UseFailureDetails);
                result = executor.Execute();

                if (addToExtendedSessions && !executor.IsCompleted)
                {
                    // addToExtendedSessions can only be set to true if extendedSessions is not null
                    extendedSessions!.Set<ExtendedSessionState>(
                        request.InstanceId,
                        new(runtimeState, shim, executor),
                        new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromSeconds(extendedSessionIdleTimeoutInSeconds) });
                }
                else
                {
                    extendedSessions?.Remove(request.InstanceId);
                }
            }
        }

        P.OrchestratorResponse response = ProtoUtils.ConstructOrchestratorResponse(
            request.InstanceId,
            request.ExecutionId,
            result?.CustomStatus,
            result?.Actions,
            completionToken: string.Empty, /* doesn't apply */
            entityConversionState: null,
            orchestrationActivity: null,
            requiresHistory: requiresHistory);
        byte[] responseBytes = response.ToByteArray();
        return Convert.ToBase64String(responseBytes);
    }
}
