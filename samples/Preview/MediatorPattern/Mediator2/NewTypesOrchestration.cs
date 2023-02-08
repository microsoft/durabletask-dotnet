// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using McMaster.Extensions.CommandLineUtils;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace Preview.MediatorPattern.NewTypes;

/**
* This sample shows mediator-pattern orchestrations and activities using newly defined request types as their input. In
* this mode, the request object is the input to the task itself. The below code has no real purpose nor demonstrates
* good ways to organize orchestrations or activities. The purpose is to demonstrate how request objects can be defined
* manually and provided directly to RunAsync method.
* 
* This is just one such way to leverage the mediator-pattern. Ultimately all the request object is all that is needed,
* how it is created is flexible.
*/

public record MediatorOrchestratorRequest(string PropA, string PropB) : IOrchestrationRequest
{
    public TaskName GetTaskName() => nameof(MediatorOrchestrator2);
}

public class MediatorOrchestrator2 : TaskOrchestrator<MediatorOrchestratorRequest> // Single generic means it has no output. Only input.
{
    public override async Task RunAsync(TaskOrchestrationContext context, MediatorOrchestratorRequest input)
    {
        string output = await context.RunAsync(new MediatorSubOrchestratorRequest(input.PropA));
        await context.RunAsync(new WriteConsoleActivityRequest(output));
    }
}

public record MediatorSubOrchestratorRequest(string Value) : IOrchestrationRequest<string>
{
    public TaskName GetTaskName() => nameof(MediatorSubOrchestrator2);
}

public class MediatorSubOrchestrator2 : TaskOrchestrator<MediatorSubOrchestratorRequest, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, MediatorSubOrchestratorRequest input)
    {
        // Orchestrations create replay-safe loggers off the 
        ILogger logger = context.CreateReplaySafeLogger<MediatorSubOrchestrator2>();
        logger.LogDebug("In MySubOrchestrator");
        return context.RunAsync(new ExpandActivityRequest($"{nameof(MediatorSubOrchestrator2)}: {input.Value}"));
    }
}

public record WriteConsoleActivityRequest(string Value) : IActivityRequest<string>
{
    public TaskName GetTaskName() => nameof(WriteConsoleActivity2);
}

public class WriteConsoleActivity2 : TaskActivity<WriteConsoleActivityRequest> // Single generic means it has no output. Only input.
{
    readonly IConsole console;

    public WriteConsoleActivity2(IConsole console) // Dependency injection example.
    {
        this.console = console;
    }

    public override Task RunAsync(TaskActivityContext context, WriteConsoleActivityRequest input)
    {
        this.console.WriteLine(input.Value);
        return Task.CompletedTask;
    }
}

public record ExpandActivityRequest(string Value) : IActivityRequest<string>
{
    public TaskName GetTaskName() => nameof(ExpandActivity2);
}

public class ExpandActivity2 : TaskActivity<ExpandActivityRequest, string>
{
    readonly ILogger logger;

    public ExpandActivity2(ILogger<ExpandActivity2> logger) // Activities get logger from DI.
    {
        this.logger = logger;
    }

    public override Task<string> RunAsync(TaskActivityContext context, ExpandActivityRequest input)
    {
        this.logger.LogDebug("In ExpandActivity");
        return Task.FromResult($"Input received: {input.Value}");
    }
}