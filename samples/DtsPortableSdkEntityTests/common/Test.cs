// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.DurableTask;

namespace DtsPortableSdkEntityTests;

internal abstract class Test
{
    public virtual string Name => this.GetType().Name;

    public abstract Task RunAsync(TestContext context);

    public virtual TimeSpan Timeout => TimeSpan.FromSeconds(30);

    public virtual void Register(DurableTaskRegistry registry, IServiceCollection services)
    {
    }
}
