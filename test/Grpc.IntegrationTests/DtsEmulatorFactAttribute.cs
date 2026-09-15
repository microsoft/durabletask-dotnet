// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Grpc.Tests;

sealed class DtsEmulatorFactAttribute : FactAttribute
{
    internal const string ConnectionStringEnvironmentVariable = "DTS_EMULATOR_CONNECTION_STRING";

    public DtsEmulatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)))
        {
            this.Skip =
                $"Set {ConnectionStringEnvironmentVariable} to run tests against a DTS emulator.";
        }
    }
}
