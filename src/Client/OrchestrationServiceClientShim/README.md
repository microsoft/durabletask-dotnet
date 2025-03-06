A client implementation for `Microsoft.DurableTask`. This package includes a `DurableTaskClient` implementation for interacting with a task hub via a `DurableTask.Core.IOrchestrationServiceClient`.

Commonly used types:
- `ShimDurableTaskClient`
- `ShimDurableTaskClientOptions`

For more information, see https://github.com/microsoft/durabletask-dotnet

## Getting Started

``` CSharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// instantiate this using existing Microsoft.Azure.DurableTask packages.
IOrchestrationServiceClient orchestrationServiceClient = new AzureStorageOrchestrationService(...);
builder.Services.AddDurableTaskClient()
    .UseOrchestrationService(orchestrationService);
```
