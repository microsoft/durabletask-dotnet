// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Options;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask;

/// <summary>
/// Context passed from the host to the orchestration shim.
/// </summary>
public class WorkerContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerContext"/> class.
    /// </summary>
    /// <param name="dataConverter">The data converter to use for serializing and deserializing payloads.</param>
    /// <param name="logger">The logger to use for emitting logs.</param>
    /// <param name="services">The dependency-injection service provider.</param>
    /// <param name="timerOptions">Optional. The configuration options for durable timers.</param>
    public WorkerContext(
        DataConverter dataConverter,
        ILogger logger,
        IServiceProvider services,
        TimerOptions? timerOptions = null)
    {
        this.DataConverter = dataConverter;
        this.Logger = logger;
        this.Services = services;
        this.TimerOptions = timerOptions ?? TimerOptions.Default;
    }

    /// <summary>
    /// Gets the data converter to use for serializing and deserializing data payloads.
    /// </summary>
    public virtual DataConverter DataConverter { get; }

    /// <summary>
    /// Gets the logger to use for emitting logs.
    /// </summary>
    public virtual ILogger Logger { get; }

    /// <summary>
    /// Gets the dependency-injection service provider.
    /// </summary>
    public virtual IServiceProvider Services { get; }

    /// <summary>
    /// Gets the configuration options for durable timers.
    /// </summary>
    public virtual TimerOptions TimerOptions { get; }
}