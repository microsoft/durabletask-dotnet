// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Microsoft.DurableTask.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker.Shims;

/// <summary>
/// Shims a <see cref="ITaskActivity" /> to a <see cref="TaskActivity" />.
/// </summary>
class TaskActivityShim : TaskActivity
{
    readonly ITaskActivity implementation;
    readonly ILogger logger;
    readonly DataConverter dataConverter;
    readonly TaskName name;
    readonly IServiceProvider services;
    readonly IMiddlewareFeatures? features;
    readonly TaskActivityMiddlewarePipeline middlewarePipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskActivityShim"/> class.
    /// </summary>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="dataConverter">The data converter.</param>
    /// <param name="name">The name of the activity.</param>
    /// <param name="implementation">The activity implementation to wrap.</param>
    public TaskActivityShim(
        ILoggerFactory loggerFactory,
        DataConverter dataConverter,
        TaskName name,
        ITaskActivity implementation)
        : this(
            loggerFactory,
            dataConverter,
            name,
            implementation,
            EmptyServiceProvider.Instance,
            null,
            TaskActivityMiddlewarePipeline.Empty)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskActivityShim"/> class.
    /// </summary>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="dataConverter">The data converter.</param>
    /// <param name="name">The name of the activity.</param>
    /// <param name="implementation">The activity implementation to wrap.</param>
    /// <param name="services">The service provider for this activity invocation.</param>
    /// <param name="features">The middleware features for this activity invocation.</param>
    /// <param name="middlewarePipeline">The activity middleware pipeline.</param>
    internal TaskActivityShim(
        ILoggerFactory loggerFactory,
        DataConverter dataConverter,
        TaskName name,
        ITaskActivity implementation,
        IServiceProvider services,
        IMiddlewareFeatures? features,
        TaskActivityMiddlewarePipeline middlewarePipeline)
    {
        this.logger = Logs.CreateWorkerLogger(Check.NotNull(loggerFactory), "Activities");
        this.dataConverter = Check.NotNull(dataConverter);
        this.name = Check.NotDefault(name);
        this.implementation = Check.NotNull(implementation);
        this.services = Check.NotNull(services);
        this.features = features;
        this.middlewarePipeline = Check.NotNull(middlewarePipeline);
    }

    /// <inheritdoc/>
    public override Task<string?> RunAsync(TaskContext coreContext, string? rawInput)
        => this.RunCoreAsync(coreContext, rawInput, output => this.dataConverter.Serialize(output));

    /// <inheritdoc/>
    /// <remarks>Not used/called.</remarks>
    public override string Run(TaskContext context, string input) => throw new NotImplementedException();

    /// <summary>
    /// Runs the activity and returns the raw activity result after middleware has run.
    /// </summary>
    /// <param name="coreContext">The Durable Task Framework activity context.</param>
    /// <param name="rawInput">The raw serialized activity input.</param>
    /// <returns>The raw activity result after middleware has run.</returns>
    internal Task<object?> RunAndGetResultAsync(TaskContext coreContext, string? rawInput)
        => this.RunCoreAsync<object?>(coreContext, rawInput, output => output);

    static string? StripArrayCharacters(string? input)
    {
        if (input != null && input.StartsWith('[') && input.EndsWith(']'))
        {
            // Strip the outer bracket characters
            return input[1..^1];
        }

        return input;
    }

    async Task<TResult> RunCoreAsync<TResult>(
        TaskContext coreContext,
        string? rawInput,
        Func<object?, TResult> resultSelector)
    {
        Check.NotNull(coreContext);
        Check.NotNull(resultSelector);
        string? strippedRawInput = StripArrayCharacters(rawInput);
        object? deserializedInput = this.dataConverter.Deserialize(strippedRawInput, this.implementation.InputType);
        TaskActivityContextWrapper contextWrapper = new(coreContext, this.name);

        string instanceId = coreContext.OrchestrationInstance.InstanceId;
        this.logger.ActivityStarted(instanceId, this.name);

        try
        {
            DefaultTaskActivityMiddlewareContext middlewareContext = new(
                this.name,
                instanceId,
                this.implementation.InputType,
                deserializedInput,
                strippedRawInput,
                contextWrapper,
                this.features ?? new MiddlewareFeatureCollection(),
                this.services,
                () => this.implementation.RunAsync(contextWrapper, deserializedInput));
            await this.middlewarePipeline.RunAsync(middlewareContext);
            object? output = middlewareContext.Result;
            TResult result = resultSelector(output);
            this.logger.ActivityCompleted(instanceId, this.name);

            return result;
        }
        catch (Exception e)
        {
            this.logger.ActivityFailed(e, instanceId, this.name);
            throw;
        }
    }

    sealed class TaskActivityContextWrapper : TaskActivityContext
    {
        readonly TaskContext innerContext;
        readonly TaskName name;

        public TaskActivityContextWrapper(TaskContext taskContext, TaskName name)
        {
            this.innerContext = taskContext;
            this.name = name;
        }

        public override TaskName Name => this.name;

        public override string InstanceId => this.innerContext.OrchestrationInstance.InstanceId;
    }
}
