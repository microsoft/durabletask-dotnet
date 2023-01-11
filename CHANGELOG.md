﻿# Changelog

## v1.0.0

- Added `SuspendInstanceAsync` and `ResumeInstanceAsync` to `DurableTaskClient`.
- `TaskOrchestrationContext.CreateReplaySafeLogger` now creates `ILogger` directly (as opposed to wrapping an existing `ILogger`).
- Durable Functions class-based syntax now resolves `ITaskActivity` instances from `IServiceProvider`, if available there.
- `DurableTaskClient` methods have been touched up to ensure `CancellationToken` is included, as well as is the last parameter.
- Removed obsolete/unimplemented local lambda activity calls from `TaskOrchestrationContext`
- Input is now an optional parameter on `TaskOrchestrationContext.ContinueAsNew`
- Multi-target gRPC projects to now use `Grpc.Net.Client` when appropriate (.NET6.0 and up)

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
