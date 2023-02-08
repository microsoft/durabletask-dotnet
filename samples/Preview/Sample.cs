// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using McMaster.Extensions.CommandLineUtils;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Preview.MediatorPattern;

namespace Preview;

[Command(Name = "sample", Description = "Runs a provided sample.")]
[Subcommand(typeof(Mediator1Command), typeof(Mediator2Command))]
public class Sample
{
}

public abstract class SampleCommandBase
{
    public async Task OnExecuteAsync(IConsole console, DurableTaskClient client, CancellationToken cancellation)
    {
        IBaseOrchestrationRequest request = this.GetRequest();
        console.WriteLine($"Running {request.GetTaskName()}");
        string instanceId = await client.StartNewAsync(request, cancellation);
        console.WriteLine($"Created instance: '{instanceId}'");
        await client.WaitForInstanceCompletionAsync(instanceId, cancellation);
        console.WriteLine($"Instance completed: {instanceId}");
    }

    protected abstract IBaseOrchestrationRequest GetRequest();
}
