# Mediator Pattern

## Running this sample

First sample:
``` cli
dotnet run Preview.csproj -- mediator1
```

Second sample:
``` cli
dotnet run Preview.csproj -- mediator2
```

**NOTE**: see [dotnet run](https://learn.microsoft.com/dotnet/core/tools/dotnet-run). The `--` with a space following it is important.

## What is the mediator pattern?

> In software engineering, the mediator pattern defines an object that encapsulates how a set of objects interact. This pattern is considered to be a behavioral pattern due to the way it can alter the program's running behavior.
>
> -- [wikipedia](https://en.wikipedia.org/wiki/Mediator_pattern)

Specifically to Durable Task, this means using objects to assist with enqueueing of orchestrations, sub-orchestrations, and activities. These objects handle all of the following:

1. Defining which `TaskOrchestrator` or `TaskActivity` to run.
2. Providing the input for the task to be ran.
3. Defining the output type of the task.

The end result is the ability to invoke orchestrations and activities in a type-safe manner.

## What does it look like?

Instead of supplying the name, input, and return type of an orchestration or activity separately, instead a 'request' object is used to do all of these at once.

Example: enqueueing an activity.

Raw API:
``` CSharp
string result = await context.RunActivityAsync<string>(nameof(MyActivity), input);
```

Explicit extension method [1]:
``` csharp
string result = await context.RunMyActivityAsync(input);
```

Mediator
``` csharp
string result = await context.RunAsync(MyActivity.CreateRequest(input));

// OR - it is up to individual developers which style they prefer. Can also be mixed and matched as seen fit.

string result = await context.RunAsync(new MyActivityRequest(input));
```

[1] - while the extension method is concise, having many extension methods off the same type can make intellisense a bit unwieldy.
