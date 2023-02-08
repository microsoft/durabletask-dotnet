// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using McMaster.Extensions.CommandLineUtils;
using Microsoft.DurableTask;
using Preview.MediatorPattern.ExistingTypes;

namespace Preview.MediatorPattern;

[Command(Description = "Runs the first mediator sample")]
public class Mediator1Command : SampleCommandBase
{
    public static void Register(DurableTaskRegistry tasks)
    {
        tasks.AddActivity<ExpandActivity1>();
        tasks.AddActivity<WriteConsoleActivity1>();
        tasks.AddOrchestrator<MediatorOrchestrator1>();
        tasks.AddOrchestrator<MediatorSubOrchestrator1>();
    }

    protected override IBaseOrchestrationRequest GetRequest()
    {
        return MediatorOrchestrator1.CreateRequest("PropInputA", "PropInputB");
    }
}


