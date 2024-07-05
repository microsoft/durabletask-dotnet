// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Sidecar.App;

/// <summary>
/// Abstraction for reading from standard input. This abstraction allows tests to mock stdin.
/// </summary>
interface IInputReader
{
    /// <summary>
    /// Reads a single line from standard input.
    /// </summary>
    Task<string?> ReadLineAsync();
}
