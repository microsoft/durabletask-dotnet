// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Tests.Logging;

public class LogEntry
{
    public LogEntry(
        string category,
        LogLevel level,
        EventId eventId,
        Exception? exception,
        string message,
        object? state)
    {
        this.Category = category;
        this.LogLevel = level;
        this.EventId = eventId;
        this.Exception = exception;
        this.Message = message;
        this.Timestamp = DateTime.Now;
        this.State = state;
    }

    public string Category { get; }

    public DateTime Timestamp { get; }

    public EventId EventId { get; }

    public LogLevel LogLevel { get; }

    public Exception? Exception { get; }

    public string Message { get; }

    public object? State { get; }

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
