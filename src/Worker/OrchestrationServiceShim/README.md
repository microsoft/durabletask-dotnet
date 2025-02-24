A client implementation for `Microsoft.DurableTask`. This package includes a `DurableTaskWorker` implementation for interacting with a task hub via a `DurableTask.Core.IOrchestrationService`.

Commonly used types:
- `ShimDurableTaskWorker`
- `ShimDurableTaskWorkerOptions`

For more information, see https://github.com/microsoft/durabletask-dotnet

## Getting Started

``` CSharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// instantiate this using existing Microsoft.Azure.DurableTask packages.
IOrchestrationService orchestrationService = new AzureStorageOrchestrationService(...);
builder.Services.AddDurableTaskWorker()
    .UseOrchestrationService(orchestrationService);
```
