// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;
using DurableTask.Core;
using DurableTask.Core.Serializing;

namespace DurableTask;

public class TaskOptions
{
    // TODO: Don't expose DurableTask.Core types!
    public RetryOptions? RetryOptions { get; set; }

    public DataConverter? DataConverter { get; set; }

    public CancellationToken CancellationToken { get; set; }
}
