// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BenchmarkDotNet.Running;
using Microsoft.DurableTask;

BenchmarkSwitcher.FromAssembly(typeof(AssemblyMarker).Assembly).Run(args);

namespace Microsoft.DurableTask
{
    class AssemblyMarker { }
}
