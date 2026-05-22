// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker.Tests;

/// <summary>
/// In-memory <see cref="ILoggerFactory"/> that captures every log call so tests can assert on level + message.
/// </summary>
sealed class CapturingLoggerFactory : ILoggerFactory
{
    public List<(LogLevel Level, string Message)> Logs { get; } = [];

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this.Logs);

    public void Dispose()
    {
    }

    sealed class CapturingLogger(List<(LogLevel Level, string Message)> logs) : ILogger
    {
        readonly List<(LogLevel Level, string Message)> logs = logs;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            this.logs.Add((logLevel, formatter(state, exception)));
        }
    }
}
