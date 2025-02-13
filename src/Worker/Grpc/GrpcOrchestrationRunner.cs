// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.History;
using Google.Protobuf;
using Microsoft.DurableTask.Worker.Shims;
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
        Check.NotNullOrEmpty(encodedOrchestratorRequest);
        Check.NotNull(implementation);

        P.OrchestratorRequest request = P.OrchestratorRequest.Parser.Base64Decode<P.OrchestratorRequest>(
            encodedOrchestratorRequest);

        List<HistoryEvent> pastEvents = request.PastEvents.Select(ProtoUtils.ConvertHistoryEvent).ToList();
        IEnumerable<HistoryEvent> newEvents = request.NewEvents.Select(ProtoUtils.ConvertHistoryEvent);

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
        TaskOrchestration shim = factory.CreateOrchestration(orchestratorName, implementation, parent);
        TaskOrchestrationExecutor executor = new(runtimeState, shim, BehaviorOnContinueAsNew.Carryover, request.EntityParameters.ToCore(), ErrorPropagationMode.UseFailureDetails);
        OrchestratorExecutionResult result = executor.Execute();

        P.OrchestratorResponse response = ProtoUtils.ConstructOrchestratorResponse(
            request.InstanceId,
            result.CustomStatus,
            result.Actions,
            completionToken: string.Empty, /* doesn't apply */
            entityConversionState: null);
        byte[] responseBytes = response.ToByteArray();
        return Convert.ToBase64String(responseBytes);
    }
}
