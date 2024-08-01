// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.History;

namespace Microsoft.DurableTask.Sidecar.Dispatcher;

interface ITaskExecutor
{
    /// <summary>
    /// When implemented by a concrete type, executes an orchestrator and returns the next set of orchestrator actions.
    /// </summary>
    /// <param name="instance">The instance ID information of the orchestrator to execute.</param>
    /// <param name="pastEvents">The history events for previous executions of this orchestration instance.</param>
    /// <param name="newEvents">The history events that have not yet been executed by this orchestration instance.</param>
    /// <returns>
    /// Returns a task containing the result of the orchestrator execution. These are effectively the side-effects of the
    /// orchestrator code, such as calling activities, scheduling timers, etc.
    /// </returns>
    Task<OrchestratorExecutionResult> ExecuteOrchestrator(
        OrchestrationInstance instance,
        IEnumerable<HistoryEvent> pastEvents,
        IEnumerable<HistoryEvent> newEvents);

    /// <summary>
    /// When implemented by a concreate type, executes an activity task and returns its results.
    /// </summary>
    /// <param name="instance">The instance ID information of the orchestration that scheduled this activity task.</param>
    /// <param name="activityEvent">The metadata of the activity task execution, including the activity name and input.</param>
    /// <returns>Returns a task that contains the execution result of the activity.</returns>
    Task<ActivityExecutionResult> ExecuteActivity(
        OrchestrationInstance instance,
        TaskScheduledEvent activityEvent);
}
