// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.InProcessTestFramework;

/// <summary>
/// A test framework for running Durable Task orchestrations in-process without requiring
/// external backend services. This framework allows you to mock activities and test
/// orchestration logic directly.
/// </summary>
public sealed class InProcessTestFramework : IDisposable
{
    readonly InProcessTestOrchestrationService orchestrationService;
    readonly MockDurableTaskClient client;

    /// <summary>
    /// Initializes a new instance of the <see cref="InProcessTestFramework"/> class.
    /// </summary>
    /// <param name="loggerFactory">The logger factory for debugging and tracing.</param>
    public InProcessTestFramework(ILoggerFactory? loggerFactory = null)
    {
        this.orchestrationService = new InProcessTestOrchestrationService(loggerFactory);
        this.client = new MockDurableTaskClient(this.orchestrationService);
    }

    /// <summary>
    /// Gets the mock DurableTaskClient for scheduling orchestrations.
    /// </summary>
    public DurableTaskClient Client => this.client;

    /// <summary>
    /// Registers an orchestrator function.
    /// </summary>
    /// <param name="name">The orchestrator name.</param>
    /// <param name="orchestratorFunc">The orchestrator function.</param>
    /// <returns>This framework instance for method chaining.</returns>
    public InProcessTestFramework RegisterOrchestrator(
        string name, 
        Func<TaskOrchestrationContext, object?, Task<object?>> orchestratorFunc)
    {
        this.orchestrationService.RegisterOrchestrator(name, orchestratorFunc);
        return this;
    }

    /// <summary>
    /// Registers a typed orchestrator.
    /// </summary>
    /// <typeparam name="TInput">The input type.</typeparam>
    /// <typeparam name="TOutput">The output type.</typeparam>
    /// <param name="name">The orchestrator name.</param>
    /// <param name="orchestrator">The orchestrator instance.</param>
    /// <returns>This framework instance for method chaining.</returns>
    public InProcessTestFramework RegisterOrchestrator<TInput, TOutput>(
        string name, 
        TaskOrchestrator<TInput, TOutput> orchestrator)
    {
        this.orchestrationService.RegisterOrchestrator(name, orchestrator);
        return this;
    }

    /// <summary>
    /// Registers an activity function.
    /// </summary>
    /// <param name="name">The activity name.</param>
    /// <param name="activityFunc">The activity function.</param>
    /// <returns>This framework instance for method chaining.</returns>
    public InProcessTestFramework RegisterActivity(string name, Func<object?, Task<object?>> activityFunc)
    {
        this.orchestrationService.RegisterActivity(name, activityFunc);
        return this;
    }

    /// <summary>
    /// Registers a typed activity.
    /// </summary>
    /// <typeparam name="TInput">The input type.</typeparam>
    /// <typeparam name="TOutput">The output type.</typeparam>
    /// <param name="name">The activity name.</param>
    /// <param name="activity">The activity instance.</param>
    /// <returns>This framework instance for method chaining.</returns>
    public InProcessTestFramework RegisterActivity<TInput, TOutput>(
        string name, 
        TaskActivity<TInput, TOutput> activity)
    {
        this.orchestrationService.RegisterActivity(name, activity);
        return this;
    }



    /// <summary>
    /// Disposes the test framework.
    /// </summary>
    public void Dispose()
    {
        this.client?.DisposeAsync().AsTask().Wait();
        this.orchestrationService?.Dispose();
    }
}

