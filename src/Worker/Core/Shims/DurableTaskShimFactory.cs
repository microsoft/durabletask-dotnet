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
/// A shim factory for bridging between types from DurableTask.Core and those from Microsoft.DurableTask.Abstractions.
/// </summary>
/// <remarks>
/// This class is intended for use with alternate .NET-based durable task runtimes. It's not intended for use
/// in application code.
/// </remarks>
public class DurableTaskShimFactory
{
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

    /// <summary>
    /// Gets the default <see cref="DurableTaskShimFactory" /> with default values:
    /// <see cref="JsonDataConverter" />, <see cref="NullLoggerFactory" />, and
    /// <see cref="TimerOptions" />.
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
        => new TaskActivityShim(this.dataConverter, name, activity);

    /// <summary>
    /// Creates a <see cref="TaskActivity" /> from a delegate.
    /// </summary>
    /// <param name="name">
    /// The name of the activity. This should be the name the activity was invoked with.
    /// </param>
    /// <param name="implementation">The activity delegate to wrap.</param>
    /// <returns>A new <see cref="TaskActivity" />.</returns>
    public TaskActivity CreateActivity<TInput, TOutput>(
        TaskName name, Func<TaskActivityContext, TInput?, Task<TOutput?>> implementation)
        => this.CreateActivity(name, FuncTaskActivity.Create(implementation));

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
        OrchestrationInvocationContext context = new(
            name, this.dataConverter, this.loggerFactory, this.timerOptions, parent);
        return new TaskOrchestrationShim(context, orchestrator);
    }

    /// <summary>
    /// Creates a <see cref="TaskOrchestration" /> from a <see cref="ITaskOrchestrator" />.
    /// </summary>
    /// <param name="name">
    /// The name of the orchestration. This should be the name the orchestration was invoked with.
    /// </param>
    /// <param name="implementation">The orchestration delegate to wrap.</param>
    /// <param name="parent">The orchestration parent details or <c>null</c> if no parent.</param>
    /// <returns>A new <see cref="TaskOrchestration" />.</returns>
    public TaskOrchestration CreateOrchestration<TInput, TOutput>(
        TaskName name,
        Func<TaskOrchestrationContext, TInput?, Task<TOutput?>> implementation,
        ParentOrchestrationInstance? parent = null)
        => this.CreateOrchestration(name, FuncTaskOrchestrator.Create(implementation), parent);
}