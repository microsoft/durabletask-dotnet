// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using CommandLine;

namespace Microsoft.DurableTask.Sidecar.App;

[Verb("start", HelpText = "Start a Durable Task sidecar")]
class StartOptions
{
    [Option("interactive", HelpText = "Interactively start and manage orchestrations.")]
    public bool Interactive { get; set; }

    [Option("listenPort", HelpText = "The inbound gRPC port used to handle client requests.")]
    public int ListenPort { get; set; } = 4001;

    [Option("backend", HelpText = "Storage backend to use for the started sidecar (AzureStorage, MSSQL, Netherite, or Emulator).")]
    public BackendType BackendType { get; set; }
}
