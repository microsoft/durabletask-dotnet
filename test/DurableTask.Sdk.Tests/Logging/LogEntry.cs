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
using Microsoft.Extensions.Logging;

namespace DurableTask.Sdk.Tests.Logging;

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
