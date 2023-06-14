# Durable Task .NET Client SDK

[![Build status](https://github.com/microsoft/durabletask-dotnet/workflows/Validate%20Build/badge.svg)](https://github.com/microsoft/durabletask-dotnet/actions?workflow=Validate+Build)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)

The Durable Task .NET Client SDK is a .NET Standard 2.0 library for implementing Durable Task orchestrations and activities. It's specifically designed to connect to a "sidecar" process, such as the [Azure Functions .NET Isolated host](https://docs.microsoft.com/azure/azure-functions/dotnet-isolated-process-guide), a special purpose sidecar container, or potentially even [Dapr](https://github.com/dapr/dapr/issues/4576).

If you're looking to run fully self-hosted Durable Task Framework apps, see https://github.com/azure/durabletask.

*Current Release*: [v1.0.0](https://github.com/microsoft/durabletask-dotnet/releases/tag/v1.0.0)

## NuGet packages

The following nuget packages are available for download.

| Name | Latest version | Description |
| - | - | - |
 | Azure Functions Extension |  [![NuGet version (Microsoft.Azure.Functions.Worker.Extensions.DurableTask)](https://img.shields.io/nuget/vpre/Microsoft.Azure.Functions.Worker.Extensions.DurableTask)](https://www.nuget.org/packages/Microsoft.Azure.Functions.Worker.Extensions.DurableTask/) | For Durable Functions in .NET isolated. |
 | Abstractions SDK | [![NuGet version (Microsoft.DurableTask.Abstractions)](https://img.shields.io/nuget/vpre/Microsoft.DurableTask.Abstractions)](https://www.nuget.org/packages/Microsoft.DurableTask.Abstractions/) | Contains base abstractions for Durable. Useful for writing re-usable libraries independent of the chosen worker or client. |
 | Client SDK | [![NuGet version (Microsoft.DurableTask.Client)](https://img.shields.io/nuget/vpre/Microsoft.DurableTask.Client)](https://www.nuget.org/packages/Microsoft.DurableTask.Client/) | Contains the core client logic for interacting with a Durable backend. |
 | Client.Grpc SDK | [![NuGet version (Microsoft.DurableTask.Client.Grpc)](https://img.shields.io/nuget/vpre/Microsoft.DurableTask.Client.Grpc)](https://www.nuget.org/packages/Microsoft.DurableTask.Client.Grpc/) | The gRPC client implementation. |
 | Worker SDK | [![NuGet version (Microsoft.DurableTask.Worker)](https://img.shields.io/nuget/vpre/Microsoft.DurableTask.Worker)](https://www.nuget.org/packages/Microsoft.DurableTask.Worker/) | Contains the core worker logic for having a `IHostedService` to process durable tasks. |
 | Worker.Grpc SDK | [![NuGet version (Microsoft.DurableTask.Worker.Grpc)](https://img.shields.io/nuget/vpre/Microsoft.DurableTask.Worker.Grpc)](https://www.nuget.org/packages/Microsoft.DurableTask.Worker.Grpc/) | The gRPC worker implementation.
 | Source Generators | [![NuGet version (Microsoft.DurableTask.Generators)](https://img.shields.io/nuget/vpre/Microsoft.DurableTask.Generators)](https://www.nuget.org/packages/Microsoft.DurableTask.Generators/) | DurableTask source generators. |

## Usage with Azure Functions

This SDK can be used to build Durable Functions apps that run in the [Azure Functions .NET Isolated worker process](https://docs.microsoft.com/azure/azure-functions/dotnet-isolated-process-guide).

To get started, add the [Microsoft.Azure.Functions.Worker.Extensions.DurableTask](https://www.nuget.org/packages//Microsoft.Azure.Functions.Worker.Extensions.DurableTask) nuget package to your Function app project. Make sure you're using the latest .NET Worker SDK packages.

```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.Azure.Functions.Worker" Version="1.10.0" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.DurableTask" Version="1.0.0" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.Http" Version="3.0.13" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Sdk" Version="1.7.0" OutputItemType="Analyzer" />
    <PackageReference Include="Microsoft.DurableTask.Generators" Version="1.0.0-preview.1" OutputItemType="Analyzer" />
  </ItemGroup>
```

You can then use the following code to define a simple "Hello, cities" durable orchestration, triggered by an HTTP request.

```csharp
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace IsolatedFunctionApp1.Untyped;

static class HelloSequenceUntyped
{
    [Function(nameof(StartHelloCitiesUntyped))]
    public static async Task<HttpResponseData> StartHelloCitiesUntyped(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger(nameof(StartHelloCitiesUntyped));

        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(nameof(HelloCitiesUntyped));
        logger.LogInformation("Created new orchestration with instance ID = {instanceId}", instanceId);

        return client.CreateCheckStatusResponse(req, instanceId);
    }

    [Function(nameof(HelloCitiesUntyped))]
    public static async Task<string> HelloCitiesUntyped([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        string result = "";
        result += await context.CallActivityAsync<string>(nameof(SayHelloUntyped), "Tokyo") + " ";
        result += await context.CallActivityAsync<string>(nameof(SayHelloUntyped), "London") + " ";
        result += await context.CallActivityAsync<string>(nameof(SayHelloUntyped), "Seattle");
        return result;
    }

    [Function(nameof(SayHelloUntyped))]
    public static string SayHelloUntyped([ActivityTrigger] string cityName, FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger(nameof(SayHelloUntyped));
        logger.LogInformation("Saying hello to {name}", cityName);
        return $"Hello, {cityName}!";
    }
}
```

You can find the full sample file, including detailed comments, at [samples/AzureFunctionsApp/HelloCitiesUntyped.cs](samples/AzureFunctionsApp/HelloCitiesUntyped.cs).

### Class-based syntax

**IMPORTANT**: class based syntax in Durable Functions relies on a package reference to `Microsoft.DurableTask.Generators`. This is still in "preview" and may be subject to significant change before 1.0 or even post-1.0. It is recommended to stick with function-syntax for now.

A new feature in this version of Durable Functions for .NET Isolated is the ability to define orchestrators and activities as classes instead of as functions. When using the class-based syntax, source generators are used to generate function definitions behind the scenes to instantiate and invoke your classes.

The source generators also generate type-safe extension methods on the `client` and `context` objects, removing the need to reference other activities or orchestrations by name, or to use type parameters to declare the return type. The following sample demonstrates the same "Hello cities!" orchestration using the class-based syntax and source-generated extension methods.

```csharp
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace IsolatedFunctionApp1.Typed;

public static class HelloCitiesTypedStarter
{
    [Function(nameof(StartHelloCitiesTyped))]
    public static async Task<HttpResponseData> StartHelloCitiesTyped(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger(nameof(StartHelloCitiesTyped));

        string instanceId = await client.ScheduleNewHelloCitiesTypedInstanceAsync();
        logger.LogInformation("Created new orchestration with instance ID = {instanceId}", instanceId);

        return client.CreateCheckStatusResponse(req, instanceId);
    }
}

[DurableTask(nameof(HelloCitiesTyped))]
public class HelloCitiesTyped : TaskOrchestrator<string?, string>
{
    public async override Task<string> RunAsync(TaskOrchestrationContext context, string? input)
    {
        string result = "";
        result += await context.CallSayHelloTypedAsync("Tokyo") + " ";
        result += await context.CallSayHelloTypedAsync("London") + " ";
        result += await context.CallSayHelloTypedAsync("Seattle");
        return result;
    }
}

[DurableTask(nameof(SayHelloTyped))]
public class SayHelloTyped : TaskActivity<string, string>
{
    readonly ILogger? logger;

    public SayHelloTyped(ILoggerFactory? loggerFactory)
    {
        this.logger = loggerFactory?.CreateLogger<SayHelloTyped>();
    }

    public override Task<string> RunAsync(TaskActivityContext context, string cityName)
    {
        this.logger?.LogInformation("Saying hello to {name}", cityName);
        return Task.FromResult($"Hello, {cityName}!");
    }
}
```

You can find the full sample file, including detailed comments, at [samples/AzureFunctionsApp/HelloCitiesTyped.cs](samples/AzureFunctionsApp/HelloCitiesTyped.cs).

### Compatibility with Durable Functions in-process

This SDK is *not* compatible with Durable Functions for the .NET *in-process* worker. It only works with the newer out-of-process .NET Isolated worker.

There are also several features that aren't yet available:

* Durable Entities is not yet supported.
* APIs for calling HTTP endpoints are not yet available.
* Several instance management APIs are not yet implemented.

## Obtaining the Protobuf definitions

This project utilizes git submodules to obtain Protobuf definitions from [durabletask-protobuf](https://github.com/microsoft/durabletask-protobuf). You will need to obtain these to build the project.

To get the definitions, run `git submodule update --init --recursive`

## Contributing

This project welcomes contributions and suggestions.  Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution. For details, visit https://cla.opensource.microsoft.com.

When you submit a pull request, a CLA bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., status check, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

## Trademarks

This project may contain trademarks or logos for projects, products, or services. Authorized use of Microsoft
trademarks or logos is subject to and must follow
[Microsoft's Trademark & Brand Guidelines](https://www.microsoft.com/legal/intellectualproperty/trademarks/usage/general).
Use of Microsoft trademarks or logos in modified versions of this project must not cause confusion or imply Microsoft sponsorship.
Any use of third-party trademarks or logos are subject to those third-party's policies.
