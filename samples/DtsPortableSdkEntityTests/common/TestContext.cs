// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace DtsPortableSdkEntityTests;

internal class TestContext
{
    public TestContext(DurableTaskClient client, ILogger logger, CancellationToken cancellationToken)
    {
        this.Client = client;
        this.Logger = logger;
        this.CancellationToken = cancellationToken;
    }

    public DurableTaskClient Client { get; }

    public ILogger Logger { get; }

    public CancellationToken CancellationToken { get; set; }

    public bool BackendSupportsImplicitEntityDeletion { get; set; } = true;  // false for Azure Storage, true for Netherite, MSSQL, and DTS
}
