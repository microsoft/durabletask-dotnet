// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.DurableTask.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
    readonly IServiceProvider services;
    readonly TaskOrchestrationMiddlewarePipeline orchestrationMiddlewarePipeline;
    readonly TaskActivityMiddlewarePipeline activityMiddlewarePipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="DurableTaskShimFactory" /> class.
    /// </summary>
    /// <param name="options">The data converter.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    public DurableTaskShimFactory(
        DurableTaskWorkerOptions? options = null, ILoggerFactory? loggerFactory = null)
        : this(
            Microsoft.Extensions.Options.Options.DefaultName,
            null,
            options,
            loggerFactory,
            readOptionsFromServices: false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DurableTaskShimFactory" /> class.
    /// </summary>
    /// <param name="workerName">The name of the worker whose middleware registrations should be used.</param>
    /// <param name="services">The root service provider.</param>
    /// <param name="options">
    /// The worker options. If <c>null</c>, the named <see cref="DurableTaskWorkerOptions"/> are read from
    /// <paramref name="services"/> when available.
    /// </param>
    /// <param name="loggerFactory">
    /// The logger factory. If <c>null</c>, an <see cref="ILoggerFactory"/> is read from <paramref name="services"/>
    /// when available.
    /// </param>
    public DurableTaskShimFactory(
        string workerName,
        IServiceProvider services,
        DurableTaskWorkerOptions? options,
        ILoggerFactory? loggerFactory)
        : this(workerName, Check.NotNull(services), options, loggerFactory, readOptionsFromServices: true)
    {
    }

    DurableTaskShimFactory(
        string workerName,
        IServiceProvider? services,
        DurableTaskWorkerOptions? options,
        ILoggerFactory? loggerFactory,
        bool readOptionsFromServices)
    {
        Check.NotNull(workerName);

        this.services = services ?? EmptyServiceProvider.Instance;
        this.options = options
            ?? (readOptionsFromServices
                ? services?.GetService<IOptionsMonitor<DurableTaskWorkerOptions>>()?.Get(workerName)
                : null)
            ?? new();
        this.loggerFactory = loggerFactory
            ?? services?.GetService<ILoggerFactory>()
            ?? NullLoggerFactory.Instance;

        DurableTaskWorkerMiddlewareOptions middlewareOptions =
            services?.GetService<IOptionsMonitor<DurableTaskWorkerMiddlewareOptions>>()?.Get(workerName)
            ?? new DurableTaskWorkerMiddlewareOptions();
        this.HasOrchestrationMiddleware = middlewareOptions.OrchestrationMiddleware.Count > 0;
        this.orchestrationMiddlewarePipeline = new TaskOrchestrationMiddlewarePipeline(
            middlewareOptions.OrchestrationMiddleware.ToArray());
        this.activityMiddlewarePipeline = new TaskActivityMiddlewarePipeline(
            middlewareOptions.ActivityMiddleware.ToArray());
    }

    /// <summary>
    /// Gets the default <see cref="DurableTaskShimFactory" /> with default values.
    /// </summary>
    public static DurableTaskShimFactory Default { get; } = new(null, null);

    /// <summary>
    /// Gets a value indicating whether this factory has orchestration middleware registrations.
    /// </summary>
    public bool HasOrchestrationMiddleware { get; }

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
        return this.CreateActivity(name, activity, this.services);
    }

    /// <summary>
    /// Creates a <see cref="TaskActivity" /> from a <see cref="ITaskActivity" />.
    /// </summary>
    /// <param name="name">
    /// The name of the activity. This should be the name the activity was invoked with.
    /// </param>
    /// <param name="activity">The activity to wrap.</param>
    /// <param name="services">The service provider for this activity invocation.</param>
    /// <param name="features">The middleware features for this activity invocation.</param>
    /// <returns>A new <see cref="TaskActivity" />.</returns>
    public TaskActivity CreateActivity(
        TaskName name,
        ITaskActivity activity,
        IServiceProvider services,
        IMiddlewareFeatures? features = null)
    {
        Check.NotDefault(name);
        Check.NotNull(activity);
        Check.NotNull(services);
        return new TaskActivityShim(
            this.loggerFactory,
            this.options.DataConverter,
            name,
            activity,
            services,
            features,
            this.activityMiddlewarePipeline);
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
        OrchestrationInvocationContext context = new(
            name,
            this.options,
            this.loggerFactory,
            parent,
            this.services,
            null,
            this.orchestrationMiddlewarePipeline);
        return new TaskOrchestrationShim(context, orchestrator);
    }

    /// <summary>
    /// Creates a <see cref="TaskOrchestration" /> from a <see cref="ITaskOrchestrator" />.
    /// </summary>
    /// <param name="name">
    /// The name of the orchestration. This should be the name the orchestration was invoked with.
    /// </param>
    /// <param name="orchestrator">The orchestration to wrap.</param>
    /// <param name="services">The service provider for this orchestration invocation.</param>
    /// <param name="parent">The orchestration parent details or <c>null</c> if no parent.</param>
    /// <param name="features">The middleware features for this orchestration invocation.</param>
    /// <returns>A new <see cref="TaskOrchestration" />.</returns>
    public TaskOrchestration CreateOrchestration(
        TaskName name,
        ITaskOrchestrator orchestrator,
        IServiceProvider services,
        ParentOrchestrationInstance? parent,
        IMiddlewareFeatures? features)
    {
        Check.NotDefault(name);
        Check.NotNull(orchestrator);
        Check.NotNull(services);
        OrchestrationInvocationContext context = new(
            name,
            this.options,
            this.loggerFactory,
            parent,
            services,
            features,
            this.orchestrationMiddlewarePipeline);
        return new TaskOrchestrationShim(context, orchestrator);
    }

    /// <summary>
    /// Creates a <see cref="TaskOrchestration" /> from a <see cref="ITaskOrchestrator" />.
    /// </summary>
    /// <param name="name">
    /// The name of the orchestration. This should be the name the orchestration was invoked with.
    /// </param>
    /// <param name="orchestrator">The orchestration to wrap.</param>
    /// <param name="properties">Configuration for the orchestration.</param>
    /// <param name="parent">The orchestration parent details or <c>null</c> if no parent.</param>
    /// <returns>A new <see cref="TaskOrchestration" />.</returns>
    public TaskOrchestration CreateOrchestration(
        TaskName name,
        ITaskOrchestrator orchestrator,
        IReadOnlyDictionary<string, object?> properties,
        ParentOrchestrationInstance? parent = null)
    {
        return this.CreateOrchestration(name, orchestrator, properties, this.services, parent, features: null);
    }

    /// <summary>
    /// Creates a <see cref="TaskOrchestration" /> from a <see cref="ITaskOrchestrator" />.
    /// </summary>
    /// <param name="name">
    /// The name of the orchestration. This should be the name the orchestration was invoked with.
    /// </param>
    /// <param name="orchestrator">The orchestration to wrap.</param>
    /// <param name="properties">Configuration for the orchestration.</param>
    /// <param name="services">The service provider for this orchestration invocation.</param>
    /// <param name="parent">The orchestration parent details or <c>null</c> if no parent.</param>
    /// <param name="features">The middleware features for this orchestration invocation.</param>
    /// <returns>A new <see cref="TaskOrchestration" />.</returns>
    public TaskOrchestration CreateOrchestration(
        TaskName name,
        ITaskOrchestrator orchestrator,
        IReadOnlyDictionary<string, object?> properties,
        IServiceProvider services,
        ParentOrchestrationInstance? parent,
        IMiddlewareFeatures? features)
    {
        Check.NotDefault(name);
        Check.NotNull(orchestrator);
        Check.NotNull(properties);
        Check.NotNull(services);
        OrchestrationInvocationContext context = new(
            name,
            this.options,
            this.loggerFactory,
            parent,
            services,
            features,
            this.orchestrationMiddlewarePipeline);
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
