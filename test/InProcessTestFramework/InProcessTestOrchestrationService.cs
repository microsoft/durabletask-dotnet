// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Microsoft.DurableTask.InProcessTestFramework;

/// <summary>
/// An in-process orchestration service for testing.
/// This service manages orchestration instances without requiring external backend services.
/// </summary>
public sealed class InProcessTestOrchestrationService : IDisposable
{
    readonly ConcurrentDictionary<string, MockOrchestrationInstance> instances = new();
    readonly Dictionary<string, Func<TaskOrchestrationContext, object?, Task<object?>>> orchestratorRegistry = new();
    readonly Dictionary<string, Func<string, object?, Task<object?>>> activityRegistry = new();
    readonly ILoggerFactory loggerFactory;
    readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="InProcessTestOrchestrationService"/> class.
    /// </summary>
    /// <param name="loggerFactory">The logger factory.</param>
    public InProcessTestOrchestrationService(ILoggerFactory? loggerFactory = null)
    {
        this.loggerFactory = loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        this.logger = this.loggerFactory.CreateLogger<InProcessTestOrchestrationService>();
    }

    /// <summary>
    /// Registers an orchestrator function.
    /// </summary>
    /// <param name="name">The orchestrator name.</param>
    /// <param name="orchestratorFunc">The orchestrator function.</param>
    public void RegisterOrchestrator(string name, Func<TaskOrchestrationContext, object?, Task<object?>> orchestratorFunc)
    {
        this.orchestratorRegistry[name] = orchestratorFunc;
        this.logger.LogDebug("Registered orchestrator: {OrchestratorName}", name);
    }

    /// <summary>
    /// Registers an orchestrator from a TaskOrchestrator implementation.
    /// </summary>
    /// <typeparam name="TInput">The input type.</typeparam>
    /// <typeparam name="TOutput">The output type.</typeparam>
    /// <param name="name">The orchestrator name.</param>
    /// <param name="orchestrator">The orchestrator instance.</param>
    public void RegisterOrchestrator<TInput, TOutput>(string name, TaskOrchestrator<TInput, TOutput> orchestrator)
    {
        this.orchestratorRegistry[name] = async (context, input) =>
        {
            TInput? typedInput = input is TInput ? (TInput)input : default;
            return await orchestrator.RunAsync(context, typedInput!);
        };
        this.logger.LogDebug("Registered typed orchestrator: {OrchestratorName}", name);
    }

    /// <summary>
    /// Registers an activity function.
    /// </summary>
    /// <param name="name">The activity name.</param>
    /// <param name="activityFunc">The activity function.</param>
    public void RegisterActivity(string name, Func<object?, Task<object?>> activityFunc)
    {
        this.activityRegistry[name] = (activityName, input) => activityFunc(input);
        this.logger.LogDebug("Registered activity: {ActivityName}", name);
    }

    /// <summary>
    /// Registers an activity from a TaskActivity implementation.
    /// </summary>
    /// <typeparam name="TInput">The input type.</typeparam>
    /// <typeparam name="TOutput">The output type.</typeparam>
    /// <param name="name">The activity name.</param>
    /// <param name="activity">The activity instance.</param>
    public void RegisterActivity<TInput, TOutput>(string name, TaskActivity<TInput, TOutput> activity)
    {
        this.activityRegistry[name] = async (activityName, input) =>
        {
            TInput? typedInput = input is TInput ? (TInput)input : default;
            
            // Create activity context with real activity name
            var activityContext = new MockActivityContext(activityName);
            return await activity.RunAsync(activityContext, typedInput!);
        };
        this.logger.LogDebug("Registered typed activity: {ActivityName}", name);
    }

    /// <summary>
    /// Schedules a new orchestration instance.
    /// </summary>
    /// <param name="orchestratorName">The orchestrator name.</param>
    /// <param name="instanceId">The instance ID.</param>
    /// <param name="input">The orchestration input.</param>
    /// <param name="options">The start orchestration options.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A task that completes when the orchestration is scheduled.</returns>
    public async Task ScheduleOrchestrationAsync(
        TaskName orchestratorName,
        string instanceId,
        object? input,
        StartOrchestrationOptions? options,
        CancellationToken cancellation)
    {
        if (!this.orchestratorRegistry.TryGetValue(orchestratorName.Name, out var orchestratorFunc))
        {
            throw new InvalidOperationException($"Orchestrator '{orchestratorName.Name}' is not registered. Use RegisterOrchestrator() to register it.");
        }

        var instance = new MockOrchestrationInstance(instanceId, orchestratorName, input);

        this.instances[instanceId] = instance;
        this.logger.LogInformation("Scheduled orchestration: {InstanceId} ({OrchestratorName})", instanceId, orchestratorName.Name);

        // Start execution in the background
        _ = Task.Run(async () =>
        {
            try
            {
                await this.ExecuteOrchestrationAsync(instance, orchestratorFunc, cancellation);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Error executing orchestration {InstanceId}", instanceId);
                instance.RuntimeStatus = OrchestrationRuntimeStatus.Failed;
                instance.FailureDetails = TaskFailureDetails.FromException(ex);
                instance.LastUpdatedAt = DateTime.UtcNow;
            }
        }, cancellation);

        await Task.Delay(10, cancellation); // Small delay to allow status to update
    }

    async Task ExecuteOrchestrationAsync(
        MockOrchestrationInstance instance,
        Func<TaskOrchestrationContext, object?, Task<object?>> orchestratorFunc,
        CancellationToken cancellation)
    {
        instance.RuntimeStatus = OrchestrationRuntimeStatus.Running;
        instance.LastUpdatedAt = DateTime.UtcNow;

        this.logger.LogInformation("Starting orchestration execution: {InstanceId}", instance.InstanceId);

        var context = new InProcessTaskOrchestrationContext(
            instance.Name,
            instance.InstanceId,
            instance.Input,
            DateTime.UtcNow,
            this.loggerFactory,
            this.activityRegistry);

        // Store the context so external events can be sent to it
        instance.Context = context;

        object? result = await orchestratorFunc(context, instance.Input);

        instance.RuntimeStatus = OrchestrationRuntimeStatus.Completed;
        instance.Output = result;
        instance.LastUpdatedAt = DateTime.UtcNow;

        this.logger.LogInformation("Completed orchestration execution: {InstanceId}", instance.InstanceId);
    }

    /// <summary>
    /// Gets an orchestration instance.
    /// </summary>
    /// <param name="instanceId">The instance ID.</param>
    /// <param name="getInputsAndOutputs">Whether to include inputs and outputs.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>The orchestration metadata.</returns>
    public Task<OrchestrationMetadata> GetInstanceAsync(
        string instanceId,
        bool getInputsAndOutputs,
        CancellationToken cancellation)
    {
        if (this.instances.TryGetValue(instanceId, out var instance))
        {
            var metadata = CreateMetadata(instance, getInputsAndOutputs);
            return Task.FromResult(metadata);
        }

        throw new InvalidOperationException($"Orchestration instance '{instanceId}' not found.");
    }

    /// <summary>
    /// Waits for an orchestration instance to start.
    /// </summary>
    /// <param name="instanceId">The instance ID.</param>
    /// <param name="getInputsAndOutputs">Whether to include inputs and outputs.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>The orchestration metadata.</returns>
    public async Task<OrchestrationMetadata> WaitForInstanceStartAsync(
        string instanceId,
        bool getInputsAndOutputs,
        CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            if (this.instances.TryGetValue(instanceId, out var instance) &&
                instance.RuntimeStatus != OrchestrationRuntimeStatus.Pending)
            {
                return CreateMetadata(instance, getInputsAndOutputs);
            }

            await Task.Delay(50, cancellation);
        }

        throw new OperationCanceledException();
    }

    /// <summary>
    /// Waits for an orchestration instance to complete.
    /// </summary>
    /// <param name="instanceId">The instance ID.</param>
    /// <param name="getInputsAndOutputs">Whether to include inputs and outputs.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>The orchestration metadata.</returns>
    public async Task<OrchestrationMetadata> WaitForInstanceCompletionAsync(
        string instanceId,
        bool getInputsAndOutputs,
        CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            if (this.instances.TryGetValue(instanceId, out var instance) &&
                (instance.RuntimeStatus == OrchestrationRuntimeStatus.Completed ||
                 instance.RuntimeStatus == OrchestrationRuntimeStatus.Failed ||
                 instance.RuntimeStatus == OrchestrationRuntimeStatus.Terminated))
            {
                return CreateMetadata(instance, getInputsAndOutputs);
            }

            await Task.Delay(50, cancellation);
        }

        throw new OperationCanceledException();
    }

    /// <summary>
    /// Raises an external event for an orchestration instance.
    /// </summary>
    /// <param name="instanceId">The instance ID.</param>
    /// <param name="eventName">The event name.</param>
    /// <param name="eventPayload">The event payload.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A task that completes when the event is raised.</returns>
    public Task RaiseEventAsync(string instanceId, string eventName, object? eventPayload, CancellationToken cancellation)
    {
        if (!this.instances.TryGetValue(instanceId, out var instance))
        {
            throw new InvalidOperationException($"Orchestration instance '{instanceId}' not found");
        }

        if (instance.Context == null)
        {
            throw new InvalidOperationException($"Orchestration context not available for instance '{instanceId}'");
        }

        instance.Context.SetExternalEvent(eventName, eventPayload);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Suspends an orchestration instance.
    /// </summary>
    /// <param name="instanceId">The instance ID.</param>
    /// <param name="reason">The suspension reason.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A task that completes when the instance is suspended.</returns>
    public Task SuspendInstanceAsync(string instanceId, string? reason, CancellationToken cancellation)
    {
        if (this.instances.TryGetValue(instanceId, out var instance))
        {
            instance.RuntimeStatus = OrchestrationRuntimeStatus.Suspended;
            instance.LastUpdatedAt = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Resumes an orchestration instance.
    /// </summary>
    /// <param name="instanceId">The instance ID.</param>
    /// <param name="reason">The resume reason.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A task that completes when the instance is resumed.</returns>
    public Task ResumeInstanceAsync(string instanceId, string? reason, CancellationToken cancellation)
    {
        if (this.instances.TryGetValue(instanceId, out var instance))
        {
            instance.RuntimeStatus = OrchestrationRuntimeStatus.Running;
            instance.LastUpdatedAt = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Terminates an orchestration instance.
    /// </summary>
    /// <param name="instanceId">The instance ID.</param>
    /// <param name="output">The termination output.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A task that completes when the instance is terminated.</returns>
    public Task TerminateInstanceAsync(string instanceId, object? output, CancellationToken cancellation)
    {
        if (this.instances.TryGetValue(instanceId, out var instance))
        {
            instance.RuntimeStatus = OrchestrationRuntimeStatus.Terminated;
            instance.Output = output;
            instance.LastUpdatedAt = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }

    static OrchestrationMetadata CreateMetadata(MockOrchestrationInstance instance, bool getInputsAndOutputs)
    {
        return new OrchestrationMetadata(instance.Name.Name, instance.InstanceId)
        {
            RuntimeStatus = instance.RuntimeStatus,
            CreatedAt = instance.CreatedAt,
            LastUpdatedAt = instance.LastUpdatedAt,
            SerializedInput = getInputsAndOutputs ? instance.Input?.ToString() : null,
            SerializedOutput = getInputsAndOutputs ? instance.Output?.ToString() : null,
            SerializedCustomStatus = null,
            FailureDetails = instance.FailureDetails
        };
    }

    /// <summary>
    /// Disposes the orchestration service.
    /// </summary>
    public void Dispose()
    {
        this.instances.Clear();
    }
}
