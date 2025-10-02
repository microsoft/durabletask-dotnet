// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.DurableTask.Worker.Shims;

/// <summary>
/// A shim factory for bridging between types from DurableTask.Core and those from Microsoft.DurableTask.Abstractions.
/// </summary>
/// <remarks>
/// This class is intended for use with alternate .NET-based durable task runtimes. It's not intended for use
/// in application code.
/// </remarks>
public class DurableTaskShimFactory
{
    readonly DurableTaskWorkerOptions options;
    readonly ILoggerFactory loggerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="DurableTaskShimFactory" /> class.
    /// </summary>
    /// <param name="options">The data converter.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="exceptionPropertiesProvider">Custom provider used to extract exception properties for inclusion in the failure details.</param>
    public DurableTaskShimFactory(
        DurableTaskWorkerOptions? options = null, ILoggerFactory? loggerFactory = null)
    {
        this.options = options ?? new();
        this.loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }

    /// <summary>
    /// Gets the default <see cref="DurableTaskShimFactory" /> with default values.
    /// </summary>
    public static DurableTaskShimFactory Default { get; } = new();

    /// <summary>
    /// Creates a <see cref="TaskActivity" /> from a <see cref="ITaskActivity" />.
    /// </summary>
    /// <param name="name">
    /// The name of the activity. This should be the name the activity was invoked with.
    /// </param>
    /// <param name="activity">The activity to wrap.</param>
    /// <returns>A new <see cref="TaskActivity" />.</returns>
    public TaskActivity CreateActivity(TaskName name, ITaskActivity activity)
    {
        Check.NotDefault(name);
        Check.NotNull(activity);
        return new TaskActivityShim(this.loggerFactory, this.options.DataConverter, name, activity);
    }

    /// <summary>
    /// Creates a <see cref="TaskActivity" /> from a delegate.
    /// </summary>
    /// <param name="name">
    /// The name of the activity. This should be the name the activity was invoked with.
    /// </param>
    /// <typeparam name="TInput">The input type of the activity.</typeparam>
    /// <typeparam name="TOutput">The output type of the activity.</typeparam>
    /// <param name="implementation">The activity delegate to wrap.</param>
    /// <returns>A new <see cref="TaskActivity" />.</returns>
    public TaskActivity CreateActivity<TInput, TOutput>(
        TaskName name, Func<TaskActivityContext, TInput?, Task<TOutput?>> implementation)
    {
        Check.NotDefault(name);
        Check.NotNull(implementation);
        return this.CreateActivity(name, FuncTaskActivity.Create(implementation));
    }

    /// <summary>
    /// Creates a <see cref="TaskOrchestration" /> from a <see cref="ITaskOrchestrator" />.
    /// </summary>
    /// <param name="name">
    /// The name of the orchestration. This should be the name the orchestration was invoked with.
    /// </param>
    /// <param name="orchestrator">The orchestration to wrap.</param>
    /// <param name="parent">The orchestration parent details or <c>null</c> if no parent.</param>
    /// <returns>A new <see cref="TaskOrchestration" />.</returns>
    public TaskOrchestration CreateOrchestration(
        TaskName name, ITaskOrchestrator orchestrator, ParentOrchestrationInstance? parent = null)
    {
        Check.NotDefault(name);
        Check.NotNull(orchestrator);
        OrchestrationInvocationContext context = new(name, this.options, this.loggerFactory, parent);
        return new TaskOrchestrationShim(context, orchestrator);
    }

    /// <summary>
    /// Creates a <see cref="TaskOrchestration" /> from a <see cref="ITaskOrchestrator" />.
    /// </summary>
    /// <param name="name">
    /// The name of the orchestration. This should be the name the orchestration was invoked with.
    /// </param>
    /// <param name="orchestrator">The orchestration to wrap.</param>
    /// <param name ="properties">Configuration for the orchestration.</param>
    /// <param name="parent">The orchestration parent details or <c>null</c> if no parent.</param>
    /// <returns>A new <see cref="TaskOrchestration" />.</returns>
    public TaskOrchestration CreateOrchestration(
        TaskName name,
        ITaskOrchestrator orchestrator,
        IReadOnlyDictionary<string, object?> properties,
        ParentOrchestrationInstance? parent = null)
    {
        Check.NotDefault(name);
        Check.NotNull(orchestrator);
        Check.NotNull(properties);
        OrchestrationInvocationContext context = new(name, this.options, this.loggerFactory, parent);
        return new TaskOrchestrationShim(context, orchestrator, properties);
    }

    /// <summary>
    /// Creates a <see cref="TaskOrchestration" /> from a <see cref="ITaskOrchestrator" />.
    /// </summary>
    /// <param name="name">
    /// The name of the orchestration. This should be the name the orchestration was invoked with.
    /// </param>
    /// <typeparam name="TInput">The input type of the orchestration.</typeparam>
    /// <typeparam name="TOutput">The output type of the orchestration.</typeparam>
    /// <param name="implementation">The orchestration delegate to wrap.</param>
    /// <param name="parent">The orchestration parent details or <c>null</c> if no parent.</param>
    /// <returns>A new <see cref="TaskOrchestration" />.</returns>
    public TaskOrchestration CreateOrchestration<TInput, TOutput>(
        TaskName name,
        Func<TaskOrchestrationContext, TInput?, Task<TOutput?>> implementation,
        ParentOrchestrationInstance? parent = null)
    {
        Check.NotDefault(name);
        Check.NotNull(implementation);
        return this.CreateOrchestration(name, FuncTaskOrchestrator.Create(implementation), parent);
    }

    /// <summary>
    /// Creates a <see cref="TaskEntity" /> from a <see cref="ITaskEntity" />.
    /// </summary>
    /// <param name="name">
    /// The name of the entity. This should be the name the entity was invoked with.
    /// </param>
    /// <param name="entity">The entity to wrap.</param>
    /// <param name="entityId">The entity id for the shim.</param>
    /// <returns>A new <see cref="TaskEntity" />.</returns>
    public TaskEntity CreateEntity(TaskName name, ITaskEntity entity, EntityId entityId)
    {
        Check.NotDefault(name);
        Check.NotNull(entity);

        // For now, we simply create a new shim for each entity batch operation.
        // In the future we may consider caching those shims and reusing them, which can reduce
        // deserialization and allocation overheads.
        ILogger logger = this.loggerFactory.CreateLogger(entity.GetType());
        return new TaskEntityShim(this.options.DataConverter, entity, entityId, logger);
    }
}
