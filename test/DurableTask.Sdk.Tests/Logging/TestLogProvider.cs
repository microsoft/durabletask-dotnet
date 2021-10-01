//  ----------------------------------------------------------------------------------
//  Copyright Microsoft Corporation
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//  http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//  ----------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace DurableTask.Sdk.Tests.Logging;

public sealed class TestLogProvider : ILoggerProvider
{
    readonly ITestOutputHelper output;
    readonly ConcurrentDictionary<string, TestLogger> loggers;

    public TestLogProvider(ITestOutputHelper output)
    {
        this.output = output ?? throw new ArgumentNullException(nameof(output));
        this.loggers = new ConcurrentDictionary<string, TestLogger>(StringComparer.OrdinalIgnoreCase);
    }

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
