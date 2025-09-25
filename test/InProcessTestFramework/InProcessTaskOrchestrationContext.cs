// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.InProcessTestFramework;

/// <summary>
/// An in-process implementation of TaskOrchestrationContext that can execute activities directly.
/// This context allows orchestrations to run without requiring the full backend infrastructure.
/// </summary>
public sealed class InProcessTaskOrchestrationContext : TaskOrchestrationContext
{
    readonly Dictionary<string, Func<string, object?, Task<object?>>> activityRegistry;
    readonly Dictionary<string, object?> externalEvents = new();
    readonly Dictionary<string, object?> customStatuses = new();
    readonly ILoggerFactory loggerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="InProcessTaskOrchestrationContext"/> class.
    /// </summary>
    /// <param name="name">The orchestration name.</param>
    /// <param name="instanceId">The instance ID.</param>
    /// <param name="input">The orchestration input.</param>
    /// <param name="currentUtcDateTime">The current UTC date time.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="activityRegistry">The registry of real activities.</param>
    public InProcessTaskOrchestrationContext(
        TaskName name,
        string instanceId,
        object? input,
        DateTime? currentUtcDateTime = null,
        ILoggerFactory? loggerFactory = null,
        Dictionary<string, Func<string, object?, Task<object?>>>? activityRegistry = null)
    {
        this.Name = name;
        this.InstanceId = instanceId;
        this.Input = input;
        this.CurrentUtcDateTime = currentUtcDateTime ?? DateTime.UtcNow;
        this.loggerFactory = loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        this.IsReplaying = false;
        this.activityRegistry = activityRegistry ?? new Dictionary<string, Func<string, object?, Task<object?>>>();
    }

    /// <inheritdoc/>
    public override TaskName Name { get; }

    /// <inheritdoc/>
    public override string InstanceId { get; }

    /// <inheritdoc/>
    public override ParentOrchestrationInstance? Parent => null;

    /// <inheritdoc/>
    public override DateTime CurrentUtcDateTime { get; }

    /// <inheritdoc/>
    public override bool IsReplaying { get; }

    /// <inheritdoc/>
    protected override ILoggerFactory LoggerFactory => this.loggerFactory;

    /// <summary>
    /// Gets the orchestration input.
    /// </summary>
    public object? Input { get; }

    /// <summary>
    /// Sets an external event for the orchestration.
    /// </summary>
    /// <param name="eventName">The name of the event.</param>
    /// <param name="eventData">The event data.</param>
    public void SetExternalEvent(string eventName, object? eventData)
    {
        this.externalEvents[eventName] = eventData;
    }

    /// <inheritdoc/>
    public override T? GetInput<T>() where T : default
    {
        if (this.Input is T typedInput)
        {
            return typedInput;
        }
        return default;
    }

    /// <inheritdoc/>
    public override async Task<TResult> CallActivityAsync<TResult>(
        TaskName name, 
        object? input = null, 
        TaskOptions? options = null)
    {
        if (this.activityRegistry.TryGetValue(name.Name, out var activityFunc))
        {
            object? result = await activityFunc(name.Name, input);
            if (result is TResult typedResult)
            {
                return typedResult;
            }
            else{
                throw new InvalidOperationException($"The activity '{name.Name}' returned a result of type '{result.GetType()}' but the expected type is '{typeof(TResult)}'.");
            };
        }

        throw new InvalidOperationException($"No activity found for '{name.Name}'. Register the activity using RegisterActivity() or setup a mock using MockActivity().");
    }

    /// <inheritdoc/>
    public override Task<TResult> CallSubOrchestratorAsync<TResult>(
        TaskName orchestratorName, 
        object? input = null, 
        TaskOptions? options = null)
    {
        throw new NotSupportedException("Sub-orchestrations are not supported in the in-process context. Use the full framework for complex scenarios.");
    }

    /// <inheritdoc/>
    public override Task CreateTimer(DateTime fireAt, CancellationToken cancellationToken)
    {
        // For testing, we complete timers immediately
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override async Task<T> WaitForExternalEvent<T>(string eventName, CancellationToken cancellationToken = default)
    {
        // Keep checking until the event is available
        while (!cancellationToken.IsCancellationRequested)
        {
            if (this.externalEvents.TryGetValue(eventName, out var eventData))
            {
                if (eventData is T typedEvent)
                {
                    // Remove the event from dictionary after consuming it
                    this.externalEvents.Remove(eventName);
                    return typedEvent;
                }
            }

            // Wait a bit before checking again
            await Task.Delay(10, cancellationToken);
        }

        // If cancelled, throw
        cancellationToken.ThrowIfCancellationRequested();
        
        // This should never be reached, but needed for compiler
        return default(T)!;
    }

    /// <inheritdoc/>
    public override Guid NewGuid()
    {
        return Guid.NewGuid();
    }

    /// <inheritdoc/>
    public override void SetCustomStatus(object? customStatus)
    {
        this.customStatuses[this.InstanceId] = customStatus;
    }

    /// <inheritdoc/>
    public override void SendEvent(string instanceId, string eventName, object payload)
    {
        throw new NotSupportedException("SendEventis not supported in the in-process context.");
    }

    /// <inheritdoc/>
    public override void ContinueAsNew(object? newInput = null, bool preserveUnprocessedEvents = true)
    {
        throw new NotSupportedException("ContinueAsNew is not supported in the in-process context.");
    }

    /// <summary>
    /// Gets the custom status that was set for the given instance.
    /// </summary>
    /// <param name="instanceId">The instance ID.</param>
    /// <returns>The custom status or null if none was set.</returns>
    public object? GetCustomStatus(string instanceId)
    {
        return this.customStatuses.TryGetValue(instanceId, out var status) ? status : null;
    }

}
