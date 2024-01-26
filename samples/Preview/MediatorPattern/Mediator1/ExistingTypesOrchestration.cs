// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using McMaster.Extensions.CommandLineUtils;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace Preview.MediatorPattern.ExistingTypes;

/**
* This sample shows mediator-pattern orchestrations and activities using existing types as their inputs. In this mode,
* the request object provides a distinct separate object as the input to the task. The below code has no real purpose
* nor demonstrates good ways to organize orchestrations or activities. The purpose is to demonstrate how the static
* 'CreateRequest' method way of using the mediator pattern.
* 
* This is just one such way to leverage the mediator-pattern. Ultimately all the request object is all that is needed,
* how it is created is flexible.
*/

public class MediatorOrchestrator1 : TaskOrchestrator<MyInput> // Single generic means it has no output. Only input.
{
    public static IOrchestrationRequest CreateRequest(string propA, string propB)
        => OrchestrationRequest.Create(nameof(MediatorOrchestrator1), new MyInput(propA, propB));

    public override async Task RunAsync(TaskOrchestrationContext context, MyInput input)
    {
        string output = await context.RunAsync(MediatorSubOrchestrator1.CreateRequest(input.PropA));
        await context.RunAsync(WriteConsoleActivity1.CreateRequest(output));

        output = await context.RunAsync(ExpandActivity1.CreateRequest(input.PropB));
        await context.RunAsync(WriteConsoleActivity1.CreateRequest(output));
    }
}

public class MediatorSubOrchestrator1 : TaskOrchestrator<string, string>
{
    public static IOrchestrationRequest<string> CreateRequest(string input)
        => OrchestrationRequest.Create<string>(nameof(MediatorSubOrchestrator1), input);

    public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        // Orchestrations create replay-safe loggers off the 
        ILogger logger = context.CreateReplaySafeLogger<MediatorSubOrchestrator1>();
        logger.LogDebug("In MySubOrchestrator");
        return context.RunAsync(ExpandActivity1.CreateRequest($"{nameof(MediatorSubOrchestrator1)}: {input}"));
    }
}

public class WriteConsoleActivity1 : TaskActivity<string> // Single generic means it has no output. Only input.
{
    readonly IConsole console;

    public WriteConsoleActivity1(IConsole console) // Dependency injection example.
    {
        this.console = console;
    }

    public static IActivityRequest CreateRequest(string input)
        => ActivityRequest.Create(nameof(WriteConsoleActivity1), input);

    public override Task RunAsync(TaskActivityContext context, string input)
    {
        this.console.WriteLine(input);
        return Task.CompletedTask;
    }
}

public class ExpandActivity1 : TaskActivity<string, string>
{
    readonly ILogger logger;

    public ExpandActivity1(ILogger<ExpandActivity1> logger) // Activities get logger from DI.
    {
        this.logger = logger;
    }

    public static IActivityRequest<string> CreateRequest(string input)
        => ActivityRequest.Create<string>(nameof(ExpandActivity1), input);

    public override Task<string> RunAsync(TaskActivityContext context, string input)
    {
        this.logger.LogDebug("In ExpandActivity");
        return Task.FromResult($"Input received: {input}");
    }
}

public record MyInput(string PropA, string PropB);
