# Durable Task .NET Client SDK

[![Build status](https://github.com/microsoft/durabletask-dotnet/workflows/Validate%20Build/badge.svg)](https://github.com/microsoft/durabletask-dotnet/actions?workflow=Validate+Build)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)

The Durable Task .NET SDK is a standalone .NET library for implementing Durable Task orchestrations, activities, and entities. It's specifically designed to connect to a "sidecar" process, such as the [Azure Functions .NET Isolated host](https://docs.microsoft.com/azure/azure-functions/dotnet-isolated-process-guide), or a managed Azure endpoint, such as the [Durable Task Scheduler](https://techcommunity.microsoft.com/blog/appsonazureblog/announcing-limited-early-access-of-the-durable-task-scheduler-for-azure-durable-/4286526) (preview).

> This project is different from the [Durable Task Framework](https://github.com/azure/durabletask), which supports running fully self-hosted apps using a storage-based backend like Azure Storage or MSSQL.

## NuGet packages

The following nuget packages are available for download.

| Name | Latest version | Description |
| - | - | - |
 | Azure Functions Extension |  [![NuGet version (Microsoft.Azure.Functions.Worker.Extensions.DurableTask)](https://img.shields.io/nuget/vpre/Microsoft.Azure.Functions.Worker.Extensions.DurableTask)](https://www.nuget.org/packages/Microsoft.Azure.Functions.Worker.Extensions.DurableTask/) | For Durable Functions in .NET isolated. |
 | Abstractions SDK | [![NuGet version (Microsoft.DurableTask.Abstractions)](https://img.shields.io/nuget/vpre/Microsoft.DurableTask.Abstractions)](https://www.nuget.org/packages/Microsoft.DurableTask.Abstractions/) | Contains base abstractions for Durable. Useful for writing re-usable libraries independent of the chosen worker or client. |
 | Client SDK | [![NuGet version (Microsoft.DurableTask.Client)](https://img.shields.io/nuget/vpre/Microsoft.DurableTask.Client)](https://www.nuget.org/packages/Microsoft.DurableTask.Client/) | Contains the core client logic for interacting with a Durable backend. |
 | Client.Grpc SDK | [![NuGet version (Microsoft.DurableTask.Client.Grpc)](https://img.shields.io/nuget/vpre/Microsoft.DurableTask.Client.Grpc)](https://www.nuget.org/packages/Microsoft.DurableTask.Client.Grpc/) | The gRPC client implementation. |
 | Client.AzureManaged SDK | [![NuGet version (Microsoft.DurableTask.Worker.AzureManaged)](https://img.shields.io/nuget/vpre/Microsoft.DurableTask.Worker.AzureManaged)](https://www.nuget.org/packages/Microsoft.DurableTask.Worker.AzureManaged/) | The client implementation for use with the [Durable Task Scheduler](https://techcommunity.microsoft.com/blog/appsonazureblog/announcing-limited-early-access-of-the-durable-task-scheduler-for-azure-durable-/4286526) (preview). |
 | Worker SDK | [![NuGet version (Microsoft.DurableTask.Worker)](https://img.shields.io/nuget/vpre/Microsoft.DurableTask.Worker)](https://www.nuget.org/packages/Microsoft.DurableTask.Worker/) | Contains the core worker logic for having a `IHostedService` to process durable tasks. |
 | Worker.Grpc SDK | [![NuGet version (Microsoft.DurableTask.Worker.Grpc)](https://img.shields.io/nuget/vpre/Microsoft.DurableTask.Worker.Grpc)](https://www.nuget.org/packages/Microsoft.DurableTask.Worker.Grpc/) | The gRPC worker implementation. |
 | Worker.AzureManaged SDK | [![NuGet version (Microsoft.DurableTask.Worker.AzureManaged)](https://img.shields.io/nuget/vpre/Microsoft.DurableTask.Worker.AzureManaged)](https://www.nuget.org/packages/Microsoft.DurableTask.Worker.AzureManaged/) | The worker implementation for use with the [Durable Task Scheduler](https://techcommunity.microsoft.com/blog/appsonazureblog/announcing-limited-early-access-of-the-durable-task-scheduler-for-azure-durable-/4286526) (preview). |
 | Source Generators | [![NuGet version (Microsoft.DurableTask.Generators)](https://img.shields.io/nuget/vpre/Microsoft.DurableTask.Generators)](https://www.nuget.org/packages/Microsoft.DurableTask.Generators/) | Source generators for type-safe orchestration and activity invocations. |

## Usage with Azure Functions

This SDK can be used to build Durable Functions apps that run in the [Azure Functions .NET Isolated worker process](https://docs.microsoft.com/azure/azure-functions/dotnet-isolated-process-guide).

To get started, add the [Microsoft.Azure.Functions.Worker.Extensions.DurableTask](https://www.nuget.org/packages//Microsoft.Azure.Functions.Worker.Extensions.DurableTask) nuget package to your Function app project. Make sure you're using the latest .NET Worker SDK packages.

```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.Azure.Functions.Worker" Version="1.10.0" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.DurableTask" Version="1.2.2" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.Http" Version="3.0.13" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Sdk" Version="1.7.0" OutputItemType="Analyzer" />
    <PackageReference Include="Microsoft.DurableTask.Generators" Version="1.0.0" OutputItemType="Analyzer" />
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

### Versioned class-based orchestrators (standalone worker)

Standalone worker projects can register multiple class-based orchestrators under the same durable task name when each class declares a unique `[DurableTask(Version = "...")]`. Start a specific implementation by setting `StartOrchestrationOptions.Version`.

```csharp
[DurableTask("OrderWorkflow", Version = "v1")]
public sealed class OrderWorkflowV1 : TaskOrchestrator<int, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, int input)
        => Task.FromResult($"v1:{input}");
}

[DurableTask("OrderWorkflow", Version = "v2")]
public sealed class OrderWorkflowV2 : TaskOrchestrator<int, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, int input)
        => Task.FromResult($"v2:{input}");
}

string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
    "OrderWorkflow",
    input: 5,
    new StartOrchestrationOptions { Version = new TaskVersion("v2") });
```

Use `ContinueAsNewOptions.NewVersion` to migrate long-running orchestrations at a replay-safe boundary.

> Per-class `[DurableTask(Version = "...")]` routing composes with `DurableTaskWorkerOptions.Versioning` (or `UseVersioning(...)`). The worker-level `MatchStrategy` gates which instance versions are accepted off the wire, and the registry then dispatches each accepted work item to the implementation that exactly matches its `(name, version)`. Use them together when a single worker needs to host multiple versions of the same name.
>
> Azure Functions projects do not support same-name multi-version class-based orchestrators in v1. The source generator reports a diagnostic instead of generating colliding triggers.

### Compatibility with Durable Functions in-process

This SDK is *not* compatible with Durable Functions for the .NET *in-process* worker. It only works with the newer out-of-process .NET Isolated worker.

## Usage with Durable Task Scheduler

Durable Task Scheduler provides durable execution in Azure. Durable execution is a fault-tolerant approach to running code that handles failures and interruptions through automatic retries and state persistence.

This SDK can also be used with the Durable Task Scheduler directly, without any Durable Functions dependency. For getting started, you can find documentation and samples [here](https://learn.microsoft.com/en-us/azure/azure-functions/durable/what-is-durable-task).

For runnable DTS emulator examples that demonstrate versioning, see the [WorkerVersioningSample](samples/WorkerVersioningSample/README.md) (deployment-based versioning), the [EternalOrchestrationVersionMigrationSample](samples/EternalOrchestrationVersionMigrationSample/README.md) (multi-version routing with `[DurableTask(Version = "...")]`), the [ActivityVersioningSample](samples/ActivityVersioningSample/README.md) (activity versioning with inherited defaults and explicit override support), and the [EntityWithVersionedOrchestrationSample](samples/EntityWithVersionedOrchestrationSample/README.md) (a single instance migrating v1→v2 via `ContinueAsNew(NewVersion)` while preserving entity-held state).

The [on-demand sandbox activities sample](samples/on-demand-sandbox/README.md) shows how to declare selected activities for Durable Task Scheduler (DTS)-managed on-demand sandbox execution and build the remote worker container image separately from the declarer app.

### Token audiences and Azure Government

`DurableTaskSchedulerClientOptions.ResourceId` and `DurableTaskSchedulerWorkerOptions.ResourceId`
configure the **token audience URI**, not an Azure Resource Manager resource path.
The same setting is available as `ResourceId` in a scheduler connection string, for every
authentication type. A `UseDurableTaskScheduler` configuration callback can override the
connection-string value.

| Configuration | Selected audience |
| --- | --- |
| Explicit nonempty `ResourceId` | The normalized explicit value |
| Missing, null, or empty `ResourceId`, with `REGION_NAME` starting with `usgov` or `usdod` (case-insensitive) | `https://durabletask.azure.us` |
| Otherwise | `https://durabletask.io` |

The default is resolved per options instance and retained across token refreshes and channel
recreation. Region matching uses prefixes only: `chinaeast2`, `notusgov`, and `notusdod` use
the public default. The audience is **not inferred from the service endpoint**.

Explicit values are normalized by trimming surrounding whitespace and trailing `/` characters,
removing one case-insensitive `/.default` suffix, and trimming trailing `/` characters again.
The SDK requests `<normalized-resource-id>/.default`. For example,
`https://durabletask.azure.us//.DEFAULT//` requests `https://durabletask.azure.us/.default`,
and `api://CustomAudience/resource/.DEFAULT/` requests
`api://CustomAudience/resource/.default`. Custom URI casing is preserved.
Whitespace-only values, `///`, `/.default`, and `/.DEFAULT///` throw an `ArgumentException`
instead of silently selecting a default.

**Behavior change:** applications running in government/DoD regions previously defaulted to
`https://durabletask.io`. Set `ResourceId = "https://durabletask.io"` explicitly on both client
and worker (or in their connection strings) if they must retain the public audience.

#### Configure the credential authority separately

Neither `ResourceId` nor `REGION_NAME` changes the endpoint or the credential's authority.
When supplying a `TokenCredential`, configure the authority on that credential. For example,
the following standalone client and worker configuration explicitly selects Azure Government:

```csharp
using Azure.Identity;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.DependencyInjection;

string endpoint = Environment.GetEnvironmentVariable("DURABLE_TASK_SCHEDULER_ENDPOINT")
    ?? throw new InvalidOperationException("DURABLE_TASK_SCHEDULER_ENDPOINT is not set.");
string taskHub = Environment.GetEnvironmentVariable("DURABLE_TASK_SCHEDULER_TASK_HUB")
    ?? throw new InvalidOperationException("DURABLE_TASK_SCHEDULER_TASK_HUB is not set.");

DefaultAzureCredential credential = new(new DefaultAzureCredentialOptions
{
    AuthorityHost = AzureAuthorityHosts.AzureGovernment,
});

ServiceCollection services = new();
services.AddDurableTaskClient(builder =>
    builder.UseDurableTaskScheduler(endpoint, taskHub, credential,
        options => options.ResourceId = "https://durabletask.azure.us"));
services.AddDurableTaskWorker(builder =>
    builder.UseDurableTaskScheduler(endpoint, taskHub, credential,
        options => options.ResourceId = "https://durabletask.azure.us"));
// Register your orchestrations and activities on the worker before starting the host.
```

For SDK-created credentials, the connection string accepts an independent `AuthorityHost`:

```text
Endpoint=https://<government-scheduler-endpoint>;TaskHub=<task-hub>;Authentication=DefaultAzure;ResourceId=https://durabletask.azure.us;AuthorityHost=https://login.microsoftonline.us/
```

`AuthorityHost` must be an absolute HTTPS URI. It is forwarded for `DefaultAzure`,
`WorkloadIdentity`, `Environment`, `VisualStudio`, `VisualStudioCode`, and `InteractiveBrowser`.
Omitting it (or leaving it empty) preserves Azure Identity defaults, including
`AZURE_AUTHORITY_HOST` where applicable. It is not applied to `ManagedIdentity`, `AzureCLI`,
`AzurePowerShell`, or `None`. Managed identity uses the hosting environment's identity
endpoint. Developer-tool credentials, including those in `DefaultAzureCredential`, may
require separate tool cloud configuration (for example, `az cloud set --name AzureUSGovernment`
before signing in with Azure CLI).

On-demand sandbox management reuses the configured client channel and audience. Sandbox
workers and their registration/reconnect streams share the worker channel and audience;
`UseSandboxWorker()` resolves the same region default. To override it, configure the
corresponding `DurableTaskSchedulerWorkerOptions` using the options system:

```csharp
services.Configure<Microsoft.DurableTask.DurableTaskSchedulerWorkerOptions>(
    options => options.ResourceId = "https://durabletask.azure.us");
```

For a named worker, pass its name to `Configure`. Sandbox workers create a managed identity
credential, so an Entra authority override does not apply. Caller-supplied gRPC channels or
call invokers remain responsible for their own authentication.

## Obtaining the Protobuf definitions

This project utilizes protobuf definitions from [durabletask-protobuf](https://github.com/microsoft/durabletask-protobuf), which are copied (vendored) into this repository under the `src/Grpc` directory. See the corresponding [README.md](./src/Grpc/README.md) for more information about how to update the protobuf definitions.

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
