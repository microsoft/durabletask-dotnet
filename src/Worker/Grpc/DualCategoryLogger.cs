// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker.Grpc;

/// <summary>
/// A logger wrapper that emits logs to both a primary (new) category and an optional legacy category.
/// </summary>
/// <remarks>
/// This logger is used to maintain backward compatibility while transitioning to more specific logging categories.
/// When legacy categories are enabled, log messages are written to both the new specific category
/// (e.g., "Microsoft.DurableTask.Worker.Grpc") and the legacy broad category (e.g., "Microsoft.DurableTask").
/// </remarks>
sealed class DualCategoryLogger : ILogger
{
    readonly ILogger primaryLogger;
    readonly ILogger? legacyLogger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DualCategoryLogger"/> class.
    /// </summary>
    /// <param name="primaryLogger">The primary logger with the new category.</param>
    /// <param name="legacyLogger">The optional legacy logger with the old category.</param>
    public DualCategoryLogger(ILogger primaryLogger, ILogger? legacyLogger)
    {
        this.primaryLogger = Check.NotNull(primaryLogger);
        this.legacyLogger = legacyLogger;
    }

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        IDisposable? primaryScope = this.primaryLogger.BeginScope(state);
        IDisposable? legacyScope = this.legacyLogger?.BeginScope(state);

        if (primaryScope is not null && legacyScope is not null)
        {
            return new CompositeDisposable(primaryScope, legacyScope);
        }

        return primaryScope ?? legacyScope;
    }

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel)
    {
        // Return true if either logger is enabled at this level
        return this.primaryLogger.IsEnabled(logLevel) ||
               (this.legacyLogger?.IsEnabled(logLevel) ?? false);
    }

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        // Log to primary logger
        if (this.primaryLogger.IsEnabled(logLevel))
        {
            this.primaryLogger.Log(logLevel, eventId, state, exception, formatter);
        }

        // Log to legacy logger if enabled
        if (this.legacyLogger?.IsEnabled(logLevel) ?? false)
        {
            this.legacyLogger.Log(logLevel, eventId, state, exception, formatter);
        }
    }

    sealed class CompositeDisposable : IDisposable
    {
        readonly IDisposable first;
        readonly IDisposable second;

        public CompositeDisposable(IDisposable first, IDisposable second)
        {
            this.first = first;
            this.second = second;
        }

        public void Dispose()
        {
            this.first.Dispose();
            this.second.Dispose();
        }
    }
}
