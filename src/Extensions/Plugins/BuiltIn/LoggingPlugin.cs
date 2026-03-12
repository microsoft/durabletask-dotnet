// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Plugins.BuiltIn;

/// <summary>
/// A plugin that provides structured logging for orchestration and activity lifecycle events.
/// Logs are emitted at appropriate levels (Information for start/complete, Error for failures)
/// and include contextual information such as instance IDs and task names.
/// </summary>
public sealed class LoggingPlugin : IDurableTaskPlugin
{
    /// <summary>
    /// The default plugin name.
    /// </summary>
    public const string DefaultName = "Microsoft.DurableTask.Logging";

    readonly IReadOnlyList<IOrchestrationInterceptor> orchestrationInterceptors;
    readonly IReadOnlyList<IActivityInterceptor> activityInterceptors;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoggingPlugin"/> class.
    /// </summary>
    /// <param name="loggerFactory">The logger factory for creating loggers.</param>
    public LoggingPlugin(ILoggerFactory loggerFactory)
    {
        Check.NotNull(loggerFactory);
        LoggingOrchestrationInterceptor orchestrationInterceptor = new(loggerFactory.CreateLogger("DurableTask.Orchestration"));
        LoggingActivityInterceptor activityInterceptor = new(loggerFactory.CreateLogger("DurableTask.Activity"));
        this.orchestrationInterceptors = new List<IOrchestrationInterceptor> { orchestrationInterceptor };
        this.activityInterceptors = new List<IActivityInterceptor> { activityInterceptor };
    }

    /// <inheritdoc />
    public string Name => DefaultName;

    /// <inheritdoc />
    public IReadOnlyList<IOrchestrationInterceptor> OrchestrationInterceptors => this.orchestrationInterceptors;

    /// <inheritdoc />
    public IReadOnlyList<IActivityInterceptor> ActivityInterceptors => this.activityInterceptors;

    /// <inheritdoc />
    public void RegisterTasks(DurableTaskRegistry registry)
    {
        // Logging plugin is cross-cutting only; it does not register any tasks.
    }

    sealed class LoggingOrchestrationInterceptor : IOrchestrationInterceptor
    {
        readonly ILogger logger;

        public LoggingOrchestrationInterceptor(ILogger logger) => this.logger = logger;

        public Task OnOrchestrationStartingAsync(OrchestrationInterceptorContext context)
        {
            this.logger.LogInformation(
                "Orchestration '{Name}' started. InstanceId: {InstanceId}",
                context.Name,
                context.InstanceId);
            return Task.CompletedTask;
        }

        public Task OnOrchestrationCompletedAsync(OrchestrationInterceptorContext context, object? result)
        {
            this.logger.LogInformation(
                "Orchestration '{Name}' completed. InstanceId: {InstanceId}",
                context.Name,
                context.InstanceId);
            return Task.CompletedTask;
        }

        public Task OnOrchestrationFailedAsync(OrchestrationInterceptorContext context, Exception exception)
        {
            this.logger.LogError(
                exception,
                "Orchestration '{Name}' failed. InstanceId: {InstanceId}",
                context.Name,
                context.InstanceId);
            return Task.CompletedTask;
        }
    }

    sealed class LoggingActivityInterceptor : IActivityInterceptor
    {
        readonly ILogger logger;

        public LoggingActivityInterceptor(ILogger logger) => this.logger = logger;

        public Task OnActivityStartingAsync(ActivityInterceptorContext context)
        {
            this.logger.LogInformation(
                "Activity '{Name}' started. InstanceId: {InstanceId}",
                context.Name,
                context.InstanceId);
            return Task.CompletedTask;
        }

        public Task OnActivityCompletedAsync(ActivityInterceptorContext context, object? result)
        {
            this.logger.LogInformation(
                "Activity '{Name}' completed. InstanceId: {InstanceId}",
                context.Name,
                context.InstanceId);
            return Task.CompletedTask;
        }

        public Task OnActivityFailedAsync(ActivityInterceptorContext context, Exception exception)
        {
            this.logger.LogError(
                exception,
                "Activity '{Name}' failed. InstanceId: {InstanceId}",
                context.Name,
                context.InstanceId);
            return Task.CompletedTask;
        }
    }
}
