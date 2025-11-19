// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Microsoft.DurableTask.Tests.Logging;

public sealed class TestLogProvider(ITestOutputHelper output) : ILoggerProvider
{
    readonly ITestOutputHelper output = output ?? throw new ArgumentNullException(nameof(output));
    readonly ConcurrentDictionary<string, TestLogger> loggers = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGetLogs(string category, out IReadOnlyCollection<LogEntry> logs)
    {
        // Get all logs for loggers that are prefixed with the category name
        // (e.g. "Microsoft.DurableTask.Worker" will return all logs for "Microsoft.DurableTask.Worker.*")
        logs = this.loggers
            .Where(kvp => kvp.Key.StartsWith(category, StringComparison.OrdinalIgnoreCase))
            .SelectMany(kvp => kvp.Value.GetLogs().ToList()) // Create a snapshot to avoid concurrent modification
            .OrderBy(log => log.Timestamp)
            .ToList();
        return logs.Count > 0;
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

    class TestLogger : ILogger
    {
        readonly string category;
        readonly ITestOutputHelper output;
        readonly List<LogEntry> entries;

        public TestLogger(string category, ITestOutputHelper output)
        {
            this.category = category;
            this.output = output;
            this.entries = new List<LogEntry>();
        }

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
                this.category,
                level,
                eventId,
                exception,
                formatter(state, exception),
                state);
            this.entries.Add(entry);

            try
            {
                this.output.WriteLine(entry.ToString());
            }
            catch (InvalidOperationException)
            {
                // Expected when tests are shutting down
            }
        }
    }
}
