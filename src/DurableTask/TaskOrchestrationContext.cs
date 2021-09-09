//  ----------------------------------------------------------------------------------
//  Copyright Microsoft Corporation
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//  http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//  ----------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace DurableTask;

// TODO: Summary and detailed description in the remarks
public abstract class TaskOrchestrationContext
{
    /// <summary>
    /// Gets the name of the task orchestration.
    /// </summary>
    public abstract TaskName Name { get; }

    /// <summary>
    /// Gets the unique ID of the current orchestration instance.
    /// </summary>
    public abstract string InstanceId { get; }

    /// <summary>
    /// Gets the current orchestration time in UTC.
    /// </summary>
    /// <remarks>
    /// The current orchestration time is stored in the orchestration history and this API will
    /// return the same value each time it is called from a particular point in the orchestration's
    /// execution. It is a deterministic, replay-safe replacement for existing .NET APIs for getting
    /// the curren time, such as <see cref="DateTime.UtcNow"/> and <see cref="DateTimeOffset.UtcNow"/>.
    /// </remarks>
    public abstract DateTime CurrentDateTimeUtc { get; }

    /// <summary>
    /// Gets a value indicating whether the orchestrator is currently replaying a previous execution.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Orchestrator functions are "replayed" after being unloaded from memory to reconstruct local variable state.
    /// During a replay, previously executed tasks will be completed automatically with previously seen values
    /// that are stored in the orchestration history. One the orchestrator reaches the point in the orchestrator
    /// where it's no longer replaying existing history, the <see cref="IsReplaying"/> property will return <c>false</c>.
    /// </para><para>
    /// You can use this property if you have logic that needs to run only when *not* replaying. For example,
    /// certain types of application logging may become too noisy when duplicated as part of replay. The
    /// application code could check to see whether the function is being replayed and then issue the log statements
    /// when this value is <c>false</c>.
    /// </para>
    /// </remarks>
    /// <value>
    /// <c>true</c> if the orchestrator is currently replaying a previous execution; otherwise <c>false</c>.
    /// </value>
    public abstract bool IsReplaying { get; }

    /// <summary>
    /// Gets the task orchestration's input.
    /// </summary>
    /// <typeparam name="T">The type of the orchestration input. This is used for deserialization.</typeparam>
    /// <returns>Returns the input deserialized into an object of type <c>T</c>.</returns>
    public abstract T? GetInput<T>();

    public virtual Task CallActivityAsync(TaskName name, object? input = null, TaskOptions? options = null)
    {
        return this.CallActivityAsync<object>(name, input, options);
    }

    public abstract Task<T> CallActivityAsync<T>(TaskName name, object? input = null, TaskOptions? options = null);

    public virtual Task CreateTimer(TimeSpan delay, CancellationToken cancellationToken)
    {
        DateTime fireAt = this.CurrentDateTimeUtc.Add(delay);
        return this.CreateTimer(fireAt, cancellationToken);
    }

    public abstract Task CreateTimer(DateTime fireAt, CancellationToken cancellationToken);

    /// <summary>
    /// Waits for an event to be raised with name <paramref name="name"/> and returns the event data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// External clients can raise events to a waiting orchestration instance using
    /// the <see cref="TaskHubClient.RaiseEventAsync(string, string, object)"/> method.
    /// </para><para>
    /// If the current orchestrator instance is not yet waiting for an event named <paramref name="eventName"/>,
    /// then the event will be saved in the orchestration instance state and dispatched immediately when
    /// <see cref="WaitForExternalEvent{T}(string, CancellationToken)"/> is called. This event saving occurs even 
    /// if the current orchestrator cancels the wait operation before the event is received.
    /// </para><para>
    /// Orchestrators can wait for the same event name multiple times, so waiting for multiple events with the same name is
    /// allowed. Each external event received by an orchestrator will complete just one task returned by this method.
    /// </para>
    /// </remarks>
    /// <param name="name">The name of the event to wait for. Event names are case-insensitive. External event names can be reused any number of times; they are not required to be unique.</param>
    /// <param name="cancelToken">A <c>CancellationToken</c> to use to abort waiting for the event.</param>
    /// <typeparam name="T">Any serializeable type that represents the event payload.</typeparam>
    /// <returns>A task that completes when the external event is received. The value of the task is the deserialized event payload.</returns>
    public abstract Task<T> WaitForExternalEvent<T>(string eventName, CancellationToken cancellationToken = default);

    public abstract void SetCustomStatus(object customStatus);

    // TODO: More
}
