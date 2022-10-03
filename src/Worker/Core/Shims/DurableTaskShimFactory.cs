// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Options;
using Microsoft.DurableTask.Shims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.DurableTask.Worker.Shims;

/// <summary>
/// A shim factory for bridging between 
/// </summary>
/// <remarks>
/// This class is intended for use with alternate .NET-based durable task runtimes. It's not intended for use
/// in application code.
/// </remarks>
public class DurableTaskShimFactory
{
    public static readonly DurableTaskShimFactory Default = new();

    readonly DataConverter dataConverter;
    readonly ILoggerFactory loggerFactory;
    readonly TimerOptions timerOptions;

    /// <summary>
    /// Initializes a new instance of <see cref="DurableTaskShimFactory" />.
    /// </summary>
    /// <param name="dataConverter">The data converter.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="timerOptions">The timer options.</param>
    public DurableTaskShimFactory(
        DataConverter? dataConverter = null,
        ILoggerFactory? loggerFactory = null,
        TimerOptions? timerOptions = null)
    {
        this.dataConverter = dataConverter ?? JsonDataConverter.Default;
        this.loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        this.timerOptions = timerOptions ?? new();
    }

    public TaskActivity CreateActivity(TaskName name, ITaskActivity activity)
    {
        return new TaskActivityShim(this.dataConverter, name, activity);
    }

    public TaskActivity CreateActivity<TInput, TOutput>(
        TaskName name, Func<TaskActivityContext, TInput?, Task<TOutput?>> implementation)
    {
        return new TaskActivityShim<TInput, TOutput>(this.dataConverter, name, implementation);
    }

    public TaskOrchestration CreateOrchestration(
        TaskName name, ITaskOrchestrator orchestrator, ParentOrchestrationInstance? parent = null)
    {
        OrchestrationInvocationContext context = new(
            name, this.dataConverter, this.loggerFactory, this.timerOptions, parent);
        return new TaskOrchestrationShim(context, orchestrator);
    }

    public TaskOrchestration CreateOrchestration<TInput, TOutput>(
        TaskName name,
        Func<TaskOrchestrationContext, TInput?, Task<TOutput?>> implementation,
        ParentOrchestrationInstance? parent = null)
    {
        return this.CreateOrchestration(name, FuncTaskOrchestrator.Create(implementation), parent);
    }
}