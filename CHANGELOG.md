# Changelog

## <version>

### Microsoft.DurableTask.Client

- Fix filter not being passed along in `PurgeAllInstancesAsync` (https://github.com/microsoft/durabletask-dotnet/pull/289)

### Microsoft.DurableTask.Abstractions

- Enable inner exception detail propagation in `TaskFailureDetails` ([#290](https://github.com/microsoft/durabletask-dotnet/pull/290))

## v1.2.2

### Microsoft.DurableTask.Abstractions

- Fix `TaskFailureDetails.IsCausedBy` to support custom exceptions and 3rd party exceptions ([#273](https://github.com/microsoft/durabletask-dotnet/pull/273))
- Microsoft.Azure.DurableTask.Core dependency increased to `2.16.2`

### Microsoft.DurableTask.Client

- Fix typo in `PurgeInstanceAsync`  in `DurableTaskClient` (https://github.com/microsoft/durabletask-dotnet/pull/264)

## v1.2.0

- Adds support to recursively terminate/purge sub-orchestrations in `GrpcDurableTaskClient` (https://github.com/microsoft/durabletask-dotnet/pull/262)

## v1.1.1

- Microsoft.Azure.DurableTask.Core dependency increased to `2.16.1`

## v1.1.0

- Microsoft.Azure.DurableTask.Core dependency increased to `2.16.0`

## v1.1.0-preview.2

- Microsoft.Azure.DurableTask.Core dependency increased to `2.16.0-preview.2`

## v1.1.0-preview.1

Adds support for durable entities. Learn more [here](https://learn.microsoft.com/azure/azure-functions/durable/durable-functions-entities?tabs=csharp).

### Microsoft.DurableTask.Abstractions

- Microsoft.Azure.DurableTask.Core dependency increased to `2.16.0-preview.1`

## v1.0.5

### Microsoft.DurableTask.Abstractions

- Microsoft.Azure.DurableTask.Core dependency increased to `2.15.0` (https://github.com/microsoft/durabletask-dotnet/pull/212)

### Microsoft.DurableTask.Worker

- Fix re-encoding of events when using `TaskOrchestrationContext.ContinueAsNew(preserveUnprocessedEvents: true)` (https://github.com/microsoft/durabletask-dotnet/pull/212)

## v1.0.4

### Microsoft.DurableTask.Worker

- Fix handling of concurrent external events with the same name (https://github.com/microsoft/durabletask-dotnet/pull/194)

## v1.0.3

### Microsoft.DurableTask.Worker

- Fix instance ID not being passed in when using retry policy (https://github.com/microsoft/durabletask-dotnet/issues/174)

### Microsoft.DurableTask.Worker.Grpc

- Add `GrpcDurableTaskWorkerOptions.CallInvoker` as an alternative to `GrpcDurableTaskWorkerOptions.Channel`

### Microsoft.DurableTask.Client.Grpc

- Add `GrpcDurableTaskClientOptions.CallInvoker` as an alternative to `GrpcDurableTaskClientOptions.Channel`

## v1.0.2

### Microsoft.DurableTask.Worker

- Fix issue with `TaskOrchestrationContext.Parent` not being set.

## v1.0.1

### Microsoft.DurableTask.Client

- Fix incorrect bounds check on `PurgeResult`
- Address typo for `DurableTaskClient.GetInstancesAsync` (incorrectly pluralized)
    - Added `GetInstanceAsync`
    - Hide `GetInstancesAsync` from editor

## v1.0.0

- Added `SuspendInstanceAsync` and `ResumeInstanceAsync` to `DurableTaskClient`.
- Rename `DurableTaskClient` methods
    - `TerminateAsync` -> `TerminateInstanceAsync`
    - `PurgeInstanceMetadataAsync` -> `PurgeInstanceAsync`
    - `PurgeInstances` -> `PurgeAllInstancesAsync`
    - `GetInstanceMetadataAsync` -> `GetInstanceAsync`
    - `GetInstances` -> `GetAllInstancesAsync`
- `TaskOrchestrationContext.CreateReplaySafeLogger` now creates `ILogger` directly (as opposed to wrapping an existing `ILogger`).
- Durable Functions class-based syntax now resolves `ITaskActivity` instances from `IServiceProvider`, if available there.
- `DurableTaskClient` methods have been touched up to ensure `CancellationToken` is included, as well as is the last parameter.
- Removed obsolete/unimplemented local lambda activity calls from `TaskOrchestrationContext`
- Input is now an optional parameter on `TaskOrchestrationContext.ContinueAsNew`
- Multi-target gRPC projects to now use `Grpc.Net.Client` when appropriate (.NET6.0 and up)

*Note: `Microsoft.DurableTask.Generators` is remaining as `preview.1`.*

## v1.0.0-rc.1

### Included Packages

Microsoft.DurableTask.Abstractions \
Microsoft.DurableTask.Client \
Microsoft.DurableTask.Client.Grpc \
Microsoft.DurableTask.Worker \
Microsoft.DurableTask.Worker.Grpc \

_see v1.0.0-preview.1 for `Microsoft.DurableTask.Generators`_

### Updates

- Refactors and splits assemblies.
  - `Microsoft.DurableTask.Abstractions`
  - `Microsoft.DurableTask.Generators`
  - `Microsoft.DurableTask.Client`
  - `Microsoft.DurableTask.Client.Grpc`
  - `Microsoft.DurableTask.Worker`
  - `Microsoft.DurableTask.Worker.Grpc`
- Added more API documentation
- Adds ability to perform multi-instance query
- Adds `PurgeInstancesMetadataAsync` and `PurgeInstancesAsync` support and implementation to `GrpcDurableTaskClient`
- Fix issue with mixed Newtonsoft.Json and System.Text.Json serialization.
- `IDurableTaskClientProvider` added to support multiple named clients.
- Added new options pattern for starting new and sub orchestrations.
- Overhauled builder API built on top of .NET generic host.
  - Now relies on dependency injection.
  - Integrates into options pattern, giving a variety of ways for user configuration.
  - Builder is now re-enterable. Multiple calls to `.AddDurableTask{Worker|Client}` with the same name will yield the exact same builder instance.

### Breaking Changes

- `Microsoft.DurableTask.Generators` reference removed.
- Added new abstract property `TaskOrchestrationContext.ParentInstance`.
- Added new abstract method `DurableTaskClient.PurgeInstancesAsync`.
- Renamed `TaskOrchestratorBase` to `TaskOrchestrator`
  - `OnRunAsync` -> `RunAsync`, forced-nullability removed.
  - Nullability can be done in generic params, ie: `MyOrchestrator : TaskOrchestrator<string?, string?>`
  - Nullability is not verified at runtime by the base class, it is up to the individual orchestrator implementations to verify their own nullability.
- Renamed `TaskActivityBase` to `TaskActivity`
  - `OnRun` removed. With both `OnRun` and `OnRunAsync`, there was no compiler error when you did not implement one. The remaining method is now marked `abstract` to force an implementation. Synchronous implementation can still be done via `Task.FromResult`.
  - `OnRunAsync` -> `RunAsync`, forced-nullability removed.
  - Nullability can be done in generic params, ie: `MyActivity : TaskActivity<string?, string?>`
  - Nullability is not verified at runtime by the base class, it is up to the individual activity implementations to verify their own nullability.
- `TaskOrchestrationContext.StartSubOrchestrationAsync` refactored:
  - `instanceId` parameter removed. Can now specify it via supplying `SubOrchestrationOptions` for `TaskOptions`.
- `TaskOptions` refactored to be a record type.
  - Builder removed.
  - Retry provided via a property `TaskRetryOptions`, which is a pseudo union-type which can be either a `RetryPolicy` or `AsyncRetryHandler`.
  - `SubOrchestrationOptions` is a derived type that can be used to specific a sub-orchestrations instanceId.
  - Helper method `.WithInstanceId(string? instanceId)` added.
- `DurableTaskClient.ScheduleNewOrchestrationInstanceAsync` refactored:
  - `instanceId` and `startAfter` wrapped into `StartOrchestrationOptions` object.
- Builder API completely overhauled. Now built entirely on top of the .NET generic host.
  - See samples for how the new API works.
  - Supports multiple workers and named-clients.
- Ability to set `TaskName.Version` removed for now. Will be added when we address versioning.
- `IDurableTaskRegistry` removed, only `DurableTaskRegistry` concrete type.
  - All lambda-methods renamed to `AddActivityFunc` and `AddOrchestratorFunc`. This was to avoid ambiguous or incorrect overload resolution with the factory methods.

## v1.0.0-preview.1

### Included Packages

Microsoft.DurableTask.Generators

### Breaking Changes

- `Microsoft.DurableTask.Generators` is now an optional package.
  - no longer automatically brought in when referencing other packages.
  - To get code generation, add `<PackageReference Include="Microsoft.DurableTask.Generators" Version="1.0.0-preview.1" PrivateAssets="All" />` to your csproj.
- `GeneratedDurableTaskExtensions.AddAllGeneratedTasks` made `internal` (from `public`)
  - This is also to avoid conflicts when multiple files have this method generated. When wanting to expose to external consumes, a new extension method can be manually authored in the desired namespace and with an appropriate name which calls this method.

## v0.4.1-beta

Initial public release
