// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker.Logging;

/// <summary>
/// An <see cref="ILoggerFactory" /> for orchestrations which provides replay-safe <see cref="ILogger" />
/// implementations.
/// </summary>
public sealed class OrchestrationLoggerFactory : ILoggerFactory
{
    readonly ILoggerFactory innerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestrationLoggerFactory"/> class.
    /// </summary>
    /// <param name="innerFactory">The inner logger factory.</param>
    internal OrchestrationLoggerFactory(ILoggerFactory innerFactory)
    {
        this.innerFactory = Check.NotNull(innerFactory);
    }

    /// <inheritdoc/>
    public void AddProvider(ILoggerProvider provider)
    {
        throw new NotSupportedException($"Adding providers to {typeof(OrchestrationLoggerFactory)} is not supported.");
    }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName)
    {
        TaskOrchestrationContext? context = TaskOrchestrationContextAccessor.Current;
        if (context is null)
        {
            throw new InvalidOperationException(
                $"{typeof(OrchestrationLoggerFactory)} can only be used from within a {typeof(ITaskOrchestrator)}.");
        }

        ILogger inner = this.innerFactory.CreateLogger(categoryName);
        return context.CreateReplaySafeLogger(inner);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // no-op
    }
}
