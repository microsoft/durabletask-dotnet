// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client.AzureManaged;

/// <summary>
/// A log line emitted by a serverless activity sandbox.
/// </summary>
/// <param name="DtsSandboxIdentifier">The DTS sandbox identifier that produced the log line.</param>
/// <param name="Timestamp">The timestamp associated with the log line.</param>
/// <param name="Stream">The output stream that produced the line, such as stdout or stderr.</param>
/// <param name="Tag">The log tag reported by the sandbox runtime.</param>
/// <param name="Message">The parsed log message.</param>
/// <param name="RawLine">The original log line.</param>
public sealed record ServerlessSandboxLogLine(
    string DtsSandboxIdentifier,
    DateTimeOffset Timestamp,
    string Stream,
    string Tag,
    string Message,
    string RawLine);
