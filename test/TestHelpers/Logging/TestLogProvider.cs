// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Microsoft.DurableTask.Tests.Logging;

public sealed class TestLogProvider(ITestOutputHelper output) : ILoggerProvider
{
    readonly ITestOutputHelper output = output ?? throw new ArgumentNullException(nameof(output));
    readonly ConcurrentDictionary<string, TestLogger> loggers
        = new ConcurrentDictionary<string, TestLogger>(StringComparer.OrdinalIgnoreCase);

    public bool TryGetLogs(string category, out IReadOnlyCollection<LogEntry> logs)
    {
        if (this.loggers.TryGetValue(category, out TestLogger? logger))
        {
            logs = logger.GetLogs();
            return true;
        }

        logs = Array.Empty<LogEntry>();
        return false;
    }

    public void Clear()
    {
        foreach (TestLogger logger in this.loggers.Values.OfType<TestLogger>())
        {
            logger.ClearLogs();
        }
    }

    ILogger ILoggerProvider.CreateLogger(string categoryName)
    {
        return this.loggers.GetOrAdd(categoryName, _ => new TestLogger(categoryName, this.output));
    }

    void IDisposable.Dispose()
    {
        // no-op
    }

    class TestLogger(string category, ITestOutputHelper output) : ILogger
    {
        readonly List<LogEntry> entries = new List<LogEntry>();

        public IReadOnlyCollection<LogEntry> GetLogs() => this.entries.AsReadOnly();

        public void ClearLogs() => this.entries.Clear();

        IDisposable ILogger.BeginScope<TState>(TState state) => null!;

        bool ILogger.IsEnabled(LogLevel logLevel) => true;

        void ILogger.Log<TState>(
            LogLevel level,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var entry = new LogEntry(
                category,
                level,
                eventId,
                exception,
                formatter(state, exception),
                state);
            this.entries.Add(entry);

            try
            {
                output.WriteLine(entry.ToString());
            }
            catch (InvalidOperationException)
            {
                // Expected when tests are shutting down
            }
        }
    }
}
