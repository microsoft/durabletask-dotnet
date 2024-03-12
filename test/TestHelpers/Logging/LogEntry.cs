// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Tests.Logging;

public class LogEntry(
    string category,
    LogLevel level,
    EventId eventId,
    Exception? exception,
    string message,
    object? state)
{
    public string Category { get; } = category;

    public DateTime Timestamp { get; } = DateTime.Now;

    public EventId EventId { get; } = eventId;

    public LogLevel LogLevel { get; } = level;

    public Exception? Exception { get; } = exception;

    public string Message { get; } = message;

    public object? State { get; } = state;

    public override string ToString()
    {
        string output = $"{this.Timestamp:o} [{this.Category}] {this.Message}";
        if (this.Exception != null)
        {
            output += Environment.NewLine + this.Exception.ToString();
        }

        return output;
    }
}
