// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using McMaster.Extensions.CommandLineUtils;
using Microsoft.DurableTask;
using Preview.MediatorPattern.NewTypes;

namespace Preview.MediatorPattern;

[Command(Description = "Runs the second mediator sample")]
public class Mediator2Command : SampleCommandBase
{
    public static void Register(DurableTaskRegistry tasks)
    {
        tasks.AddActivity<ExpandActivity2>();
        tasks.AddActivity<WriteConsoleActivity2>();
        tasks.AddOrchestrator<MediatorOrchestrator2>();
        tasks.AddOrchestrator<MediatorSubOrchestrator2>();
    }

    protected override IBaseOrchestrationRequest GetRequest()
    {
        return new MediatorOrchestratorRequest("PropA", "PropB");
    }
}


