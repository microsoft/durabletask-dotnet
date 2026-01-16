// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Defines a policy for reusing orchestration instance IDs.
/// </summary>
/// <remarks>
/// This policy determines what happens when a client attempts to create a new orchestration instance
/// with an ID that already exists. The policy consists of an action (Error, Ignore, or Terminate)
/// and a set of orchestration runtime statuses to which the action applies.
/// </remarks>
public sealed class OrchestrationIdReusePolicy
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestrationIdReusePolicy"/> class.
    /// </summary>
    /// <param name="operationStatuses">The orchestration runtime statuses to which the action applies.</param>
    /// <param name="action">The action to take when an orchestration instance with a matching status exists.</param>
    public OrchestrationIdReusePolicy(
        IEnumerable<OrchestrationRuntimeStatus> operationStatuses,
        CreateOrchestrationAction action)
    {
        Check.NotNull(operationStatuses);
        this.OperationStatuses = operationStatuses.ToArray();
        this.Action = action;
    }

    /// <summary>
    /// Gets the orchestration runtime statuses to which the action applies.
    /// </summary>
    /// <remarks>
    /// When an orchestration instance exists with one of these statuses, the specified action will be taken.
    /// For example, if the action is <see cref="CreateOrchestrationAction.Terminate"/> and the operation statuses
    /// include <see cref="OrchestrationRuntimeStatus.Running"/>, then any running instance with the same ID
    /// will be terminated before creating a new instance.
    /// </remarks>
    public IReadOnlyList<OrchestrationRuntimeStatus> OperationStatuses { get; }

    /// <summary>
    /// Gets the action to take when an orchestration instance with a matching status exists.
    /// </summary>
    public CreateOrchestrationAction Action { get; }

    /// <summary>
    /// Creates a policy that throws an error if an orchestration instance with the specified statuses already exists.
    /// </summary>
    /// <param name="statuses">The orchestration runtime statuses that should cause an error.</param>
    /// <returns>A new <see cref="OrchestrationIdReusePolicy"/> with the Error action.</returns>
    public static OrchestrationIdReusePolicy Error(params OrchestrationRuntimeStatus[] statuses)
        => new(statuses, CreateOrchestrationAction.Error);

    /// <summary>
    /// Creates a policy that ignores the request if an orchestration instance with the specified statuses already exists.
    /// </summary>
    /// <param name="statuses">The orchestration runtime statuses that should cause the request to be ignored.</param>
    /// <returns>A new <see cref="OrchestrationIdReusePolicy"/> with the Ignore action.</returns>
    public static OrchestrationIdReusePolicy Ignore(params OrchestrationRuntimeStatus[] statuses)
        => new(statuses, CreateOrchestrationAction.Ignore);

    /// <summary>
    /// Creates a policy that terminates any existing orchestration instance with the specified statuses and creates a new one.
    /// </summary>
    /// <param name="statuses">The orchestration runtime statuses that should be terminated before creating a new instance.</param>
    /// <returns>A new <see cref="OrchestrationIdReusePolicy"/> with the Terminate action.</returns>
    public static OrchestrationIdReusePolicy Terminate(params OrchestrationRuntimeStatus[] statuses)
        => new(statuses, CreateOrchestrationAction.Terminate);
}
