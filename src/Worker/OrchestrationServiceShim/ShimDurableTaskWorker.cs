// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using DurableTask.Core;
using DurableTask.Core.Entities;
using DurableTask.Core.Entities.OperationFormat;
using DurableTask.Core.History;
using DurableTask.Core.Middleware;
using Microsoft.DurableTask.Entities;
using Microsoft.DurableTask.Worker.Hosting;
using Microsoft.DurableTask.Worker.OrchestrationServiceShim.Core;
using Microsoft.DurableTask.Worker.Shims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IOrchestrationService = DurableTask.Core.IOrchestrationService;

namespace Microsoft.DurableTask.Worker.OrchestrationServiceShim;

/// <summary>
/// A <see cref="DurableTaskWorker" /> which uses a <see cref="IOrchestrationService"/>.
/// </summary>
class ShimDurableTaskWorker : DurableTaskWorker
{
    readonly ShimDurableTaskWorkerOptions options;
    readonly IServiceProvider services;
    readonly DurableTaskShimFactory shimFactory;
    readonly ILogger logger;
    readonly TaskHubWorker worker;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShimDurableTaskWorker" /> class.
    /// </summary>/// <param name="name">The name of this worker.</param>
    /// <param name="factory">The <see cref="IDurableTaskFactory"/>.</param>
    /// <param name="options">The options for this worker.</param>
    /// <param name="services">The service provider.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    public ShimDurableTaskWorker(
        string? name,
        IDurableTaskFactory factory,
        IOptionsMonitor<ShimDurableTaskWorkerOptions> options,
        IServiceProvider services,
        ILoggerFactory loggerFactory)
        : base(name, factory)
    {
        this.options = Check.NotNull(options).Get(name);
        this.services = Check.NotNull(services);
        this.shimFactory = new(this.options, loggerFactory);
        this.logger = loggerFactory.CreateLogger<ShimDurableTaskWorker>();

        // This should already be validated by options.
        IOrchestrationService service = Verify.NotNull(this.options.Service);
        this.worker = service is IEntityOrchestrationService entity
            ? this.CreateWorker(entity, loggerFactory) : this.CreateWorker(service, loggerFactory);
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await this.worker.StopAsync();
    }

    /// <inheritdoc/>
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return this.worker.StartAsync().WaitAsync(stoppingToken);
    }

    TaskHubWorker CreateWorker(IOrchestrationService service, ILoggerFactory loggerFactory)
    {
        TaskHubWorker worker = new(
            service, new ShimOrchestrationManager(), new ShimActivityManager(), loggerFactory);
        worker.AddActivityDispatcherMiddleware(this.InvokeActivityAsync);
        worker.AddOrchestrationDispatcherMiddleware(this.InvokeOrchestrationAsync);

        return worker;
    }

    TaskHubWorker CreateWorker(IEntityOrchestrationService service, ILoggerFactory loggerFactory)
    {
        if (!this.options.EnableEntitySupport)
        {
            this.logger.EntitiesDisabled();
            return this.CreateWorker(new OrchestrationServiceNoEntities(service), loggerFactory);
        }

        if (this.Factory is not IDurableTaskFactory2)
        {
            this.logger.TaskFactoryDoesNotSupportEntities();
            return this.CreateWorker(new OrchestrationServiceNoEntities(service), loggerFactory);
        }

        TaskHubWorker worker = new(
            service,
            new ShimOrchestrationManager(),
            new ShimActivityManager(),
            new ShimEntityManager(),
            loggerFactory);
        worker.AddActivityDispatcherMiddleware(this.InvokeActivityAsync);
        worker.AddOrchestrationDispatcherMiddleware(this.InvokeOrchestrationAsync);
        worker.AddEntityDispatcherMiddleware(this.InvokeEntityAsync);

        return worker;
    }

    async Task InvokeActivityAsync(DispatchMiddlewareContext context, Func<Task> next)
    {
        Check.NotNull(context);
        Check.NotNull(next);

        TaskScheduledEvent scheduled = context.GetProperty<TaskScheduledEvent>();
        if (scheduled.Name is null)
        {
            throw new InvalidOperationException("TaskScheduledEvent.Name is not set.");
        }

        TaskName name = new(scheduled.Name);
        TaskActivity coreActivity = context.GetProperty<TaskActivity>();
        if (coreActivity is not ShimTaskActivity shimActivity)
        {
            throw new InvalidOperationException("TaskActivity is not a ShimTaskActivity.");
        }

        await using AsyncServiceScope scope = this.services.CreateAsyncScope();
        if (!this.Factory.TryCreateActivity(name, scope.ServiceProvider, out ITaskActivity? activity))
        {
            throw new InvalidOperationException($"Activity not found: {name}");
        }

        shimActivity.SetInnerActivity(this.shimFactory.CreateActivity(name, activity));
    }

    async Task InvokeOrchestrationAsync(DispatchMiddlewareContext context, Func<Task> next)
    {
        Check.NotNull(context);
        Check.NotNull(next);

        OrchestrationRuntimeState runtimeState = context.GetProperty<OrchestrationRuntimeState>();

        TaskName name = new(runtimeState.Name);
        await using AsyncServiceScope scope = this.services.CreateAsyncScope();
        if (!this.Factory.TryCreateOrchestrator(name, scope.ServiceProvider, out ITaskOrchestrator? orchestrator))
        {
            throw new InvalidOperationException($"Orchestrator not found: {name}");
        }

        ParentOrchestrationInstance? parent = runtimeState.ParentInstance is { } p ?
            new ParentOrchestrationInstance(p.Name, p.OrchestrationInstance.InstanceId) : null;

        TaskOrchestrationExecutor executor = new(
            runtimeState,
            this.shimFactory.CreateOrchestration(name, orchestrator, parent),
            BehaviorOnContinueAsNew.Carryover,
            ErrorPropagationMode.UseFailureDetails);

        OrchestratorExecutionResult result = executor.Execute();
        context.SetProperty(result);
        await next();
    }

    async Task InvokeEntityAsync(DispatchMiddlewareContext context, Func<Task> next)
    {
        Check.NotNull(context);
        Check.NotNull(next);

        EntityBatchRequest request = context.GetProperty<EntityBatchRequest>();
        if (request?.InstanceId is null)
        {
            throw new InvalidOperationException("EntityBatchRequest.InstanceId is not set.");
        }

        EntityId entityId = EntityId.FromString(request.InstanceId);
        IDurableTaskFactory2 factory = (IDurableTaskFactory2)this.Factory; // verified castable at startup.
        await using AsyncServiceScope scope = this.services.CreateAsyncScope();
        if (!factory.TryCreateEntity(entityId.Name, this.services, out ITaskEntity? entity))
        {
            throw new InvalidOperationException($"Entity not found: {entityId.Name}");
        }

        TaskEntity shim = this.shimFactory.CreateEntity(entityId.Name, entity, entityId);
        EntityBatchResult result = await shim.ExecuteOperationBatchAsync(request);
        context.SetProperty(result);
        await next();
    }
}
