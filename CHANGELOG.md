# Changelog

## 1.20.1
- Fix GrpcChannel handle leak in AzureManaged backendby nytian ([#629](https://github.com/microsoft/durabletask-dotnet/pull/629))

## 1.20.0
- Partial orchestration workitem completion support (merge after next dts dp release) by wangbill ([#514](https://github.com/microsoft/durabletask-dotnet/pull/514))
- Export history job by wangbill ([#494](https://github.com/microsoft/durabletask-dotnet/pull/494))
- Add dependency injection support to durabletasktesthost by Naiyuan Tian ([#613](https://github.com/microsoft/durabletask-dotnet/pull/613))

## v1.19.1
- Throw an `InvalidOperationException` for purge requests on running orchestrations by sophiatev ([#611](https://github.com/microsoft/durabletask-dotnet/pull/611))
- Validate c# identifiers in durabletask source generator by Copilot ([#578](https://github.com/microsoft/durabletask-dotnet/pull/578))
- Document orchestration discovery and method probing behavior in analyzers by Copilot ([#594](https://github.com/microsoft/durabletask-dotnet/pull/594))

## v1.19.0
- Extended sessions for entities in .net isolated by sophiatev ([#507](https://github.com/microsoft/durabletask-dotnet/pull/507))
- Adding the ability to specify tags and a retry policy for suborchestrations by sophiatev ([#603](https://github.com/microsoft/durabletask-dotnet/pull/603))
- Improve durabletask source generator detection and add optional project type configuration by Copilot ([#575](https://github.com/microsoft/durabletask-dotnet/pull/575))
- Add timeprovider support to orchestration analyzer by Copilot ([#573](https://github.com/microsoft/durabletask-dotnet/pull/573))
- Expand azure functions smoke tests to cover source generator scenarios by Copilot ([#604](https://github.com/microsoft/durabletask-dotnet/pull/604))
- Fix "syntaxtree is not part of the compilation" exception in orchestration analyzers by Copilot ([#588](https://github.com/microsoft/durabletask-dotnet/pull/588))
- Add waitforexternalevent overload with timeout and cancellation token by Copilot ([#555](https://github.com/microsoft/durabletask-dotnet/pull/555))
- Fix source generator for void-returning activity functions by Copilot ([#554](https://github.com/microsoft/durabletask-dotnet/pull/554))

## v1.18.2
- Add copy constructors to TaskOptions and sub-classes by halspang ([#587](https://github.com/microsoft/durabletask-dotnet/pull/587))
- Change FunctionNotFound analyzer severity to Info for cross-assembly scenarios by Copilot ([#584](https://github.com/microsoft/durabletask-dotnet/pull/584))
- Add Roslyn analyzer for non-contextual logger usage in orchestrations (DURABLE0010) by Copilot ([#553](https://github.com/microsoft/durabletask-dotnet/pull/553))
- Add specific logging categories for Worker.Grpc and orchestration logs with backward-compatible opt-in by Copilot ([#583](https://github.com/microsoft/durabletask-dotnet/pull/583))
- Fix flaky integration test race condition in dedup status check by Copilot ([#579](https://github.com/microsoft/durabletask-dotnet/pull/579))
- Add analyzer to suggest input parameter binding over GetInput() by Copilot ([#550](https://github.com/microsoft/durabletask-dotnet/pull/550))
- Add strongly-typed external events with DurableEventAttribute by Copilot ([#549](https://github.com/microsoft/durabletask-dotnet/pull/549))
- Fix orchestration analyzer to detect non-function orchestrations correctly by Copilot ([#572](https://github.com/microsoft/durabletask-dotnet/pull/572))
- Fix race condition in WaitForInstanceAsync causing intermittent test failures by Copilot ([#574](https://github.com/microsoft/durabletask-dotnet/pull/574))
- Add HelpLinkUri to Roslyn analyzer diagnostics by Copilot ([#548](https://github.com/microsoft/durabletask-dotnet/pull/548))
- Add DateTimeOffset.Now and DateTimeOffset.UtcNow detection to Roslyn analyzer by Copilot ([#547](https://github.com/microsoft/durabletask-dotnet/pull/547))
- Bump Google.Protobuf from 3.33.1 to 3.33.2 by dependabot[bot] ([#569](https://github.com/microsoft/durabletask-dotnet/pull/569))
- Add integration test coverage for Suspend/Resume operations by Copilot ([#546](https://github.com/microsoft/durabletask-dotnet/pull/546))
- Bump coverlet.collector from 6.0.2 to 6.0.4 by dependabot[bot] ([#527](https://github.com/microsoft/durabletask-dotnet/pull/527))
- Bump FluentAssertions from 6.12.1 to 6.12.2 by dependabot[bot] ([#528](https://github.com/microsoft/durabletask-dotnet/pull/528))
- Add Azure Functions smoke tests with Docker CI automation by Copilot ([#545](https://github.com/microsoft/durabletask-dotnet/pull/545))
- Bump dotnet-sdk from 10.0.100 to 10.0.101 by dependabot[bot] ([#568](https://github.com/microsoft/durabletask-dotnet/pull/568))
- Add scheduled auto-closure for stale "Needs Author Feedback" issues by Copilot ([#566](https://github.com/microsoft/durabletask-dotnet/pull/566))

## v1.18.1
- Support dedup status when starting orchestration by wangbill ([#542](https://github.com/microsoft/durabletask-dotnet/pull/542))
- Add 404 exception handling in blobpayloadstore.downloadasync by Copilot ([#534](https://github.com/microsoft/durabletask-dotnet/pull/534))
- Bump analyzers version to 0.2.0 by Copilot ([#552](https://github.com/microsoft/durabletask-dotnet/pull/552))
- Add integration tests for exception type handling by Copilot ([#544](https://github.com/microsoft/durabletask-dotnet/pull/544))
- Add roslyn analyzer to detect calls to non-existent functions (name mismatch) by Copilot ([#530](https://github.com/microsoft/durabletask-dotnet/pull/530))
- Remove preview suffix by Copilot ([#541](https://github.com/microsoft/durabletask-dotnet/pull/541))
- Add xml documentation with see cref links to generated code for better ide navigation by Copilot ([#535](https://github.com/microsoft/durabletask-dotnet/pull/535))
- Add entity source generation support for durable functions by Copilot ([#533](https://github.com/microsoft/durabletask-dotnet/pull/533))

## v1.18.0
- Add taskentity support to durabletasksourcegenerator by Copilot ([#517](https://github.com/microsoft/durabletask-dotnet/pull/517))
- Bump azure.identity by dependabot[bot] ([#525](https://github.com/microsoft/durabletask-dotnet/pull/525))
- Bump google.protobuf by dependabot[bot] ([#529](https://github.com/microsoft/durabletask-dotnet/pull/529))
- Configure dependabot for dotnet-sdk updates by Tomer Rosenthal ([#524](https://github.com/microsoft/durabletask-dotnet/pull/524))
- Add code review guidelines to copilot-instructions.md by Copilot ([#522](https://github.com/microsoft/durabletask-dotnet/pull/522))
- Remove webapi sample by sophiatev ([#520](https://github.com/microsoft/durabletask-dotnet/pull/520))
- Fix functioncontext check and polymorphic type conversions in activity analyzer by Naiyuan Tian ([#506](https://github.com/microsoft/durabletask-dotnet/pull/506))
- Align waitforexternalevent waiter picking order to lifo by wangbill ([#509](https://github.com/microsoft/durabletask-dotnet/pull/509))
- Update project to support .net 6.0 alongside .net 8.0 and .net 10 by Tomer Rosenthal ([#512](https://github.com/microsoft/durabletask-dotnet/pull/512))
- Update project to target .net 8.0 and .net 10 and upgrade dependencies by Tomer Rosenthal ([#510](https://github.com/microsoft/durabletask-dotnet/pull/510))
- Support worker features announcement by wangbill ([#502](https://github.com/microsoft/durabletask-dotnet/pull/502))
- Introduce custom copilot review instructions by halspang ([#503](https://github.com/microsoft/durabletask-dotnet/pull/503))
- Add API to get orchestration history ([#516](https://github.com/microsoft/durabletask-dotnet/pull/516))

## v1.17.1
- Fix Worker Registry and Add Documentation Notes by nytian in [#462](https://github.com/microsoft/durabletask-dotnet/pull/495)
- Initial attempt to fix carryover events issue on continue-as-new by cgillum in [#496](https://github.com/microsoft/durabletask-dotnet/pull/496)
- Fix encoding of entity unlock events by sebastianburckhardt in [#462](https://github.com/microsoft/durabletask-dotnet/pull/462)

## v1.17.0
-Add Microsoft.DurableTask.Extensions.AzureBlobPayloads to nuget publish by YunchuWang in [#488](https://github.com/microsoft/durabletask-dotnet/pull/488)
-Add API for In-process Testing and Add Class-Syntax Integration Tests by nytian in [#476](https://github.com/microsoft/durabletask-dotnet/pull/476)
-Fix Purge Instance Comments by sophiatev in [#489](https://github.com/microsoft/durabletask-dotnet/pull/489)
-Fix ServiceCollectionExtensions.AddDurableTaskClient by sophiatev in [#490](https://github.com/microsoft/durabletask-dotnet/pull/490)
-Update zuremanaged sdks to official version by YunchuWang in [#493](https://github.com/microsoft/durabletask-dotnet/pull/493)
-Add Rewind to .NET isolated by sophiatev in [#479](https://github.com/microsoft/durabletask-dotnet/pull/479)
-Add tags field to CompleteOrchestratorAction by sophiatev in [#492](https://github.com/microsoft/durabletask-dotnet/pull/492)

## v1.16.2
- Generate changelog script + update changelog for v1.16.1 by wangbill ([#486](https://github.com/microsoft/durabletask-dotnet/pull/486))
- Remove unnecessary project reference to grpc.azuremanagedbackend in azureblobpayloads.csproj by wangbill ([#485](https://github.com/microsoft/durabletask-dotnet/pull/485))
- Large payload azure blob externalization support by wangbill ([#468](https://github.com/microsoft/durabletask-dotnet/pull/468))

## v1.16.1
- Include exception properties in failure details when orchestration throws directly by Naiyuan Tian ([#482](https://github.com/microsoft/durabletask-dotnet/pull/482))
- Set low priority for scheduled runs by Daniel Castro ([#477](https://github.com/microsoft/durabletask-dotnet/pull/477))

## v1.16.0
- Include Exception Properties at FailureDetails by nytian in([#474](https://github.com/microsoft/durabletask-dotnet/pull/474))

## v1.15.1 
- Add version check to activities by @halspang in ([#472](https://github.com/microsoft/durabletask-dotnet/pull/472))

## v1.15.0
- Abandon workitem if processing workitem failed by @YunchuWang in ([#467](https://github.com/microsoft/durabletask-dotnet/pull/467))
- Extended Sessions for Isolated (Orchestrations) by @sophiatev in ([#449](https://github.com/microsoft/durabletask-dotnet/pull/449))
  
## v1.14.0
- Add RestartAsync API Support at DurableTaskClient ([#456](https://github.com/microsoft/durabletask-dotnet/pull/456))

## v1.13.0
- Add orchestration execution tracing ([#441](https://github.com/microsoft/durabletask-dotnet/pull/441))

## v1.12.0

- Activity tag support ([#426](https://github.com/microsoft/durabletask-dotnet/pull/426))
- Adding Analyzer to build and release ([#444](https://github.com/microsoft/durabletask-dotnet/pull/444))
- Add ability to filter orchestrations at worker ([#443](https://github.com/microsoft/durabletask-dotnet/pull/443))
- Removing breaking change for TaskOptions ([#446](https://github.com/microsoft/durabletask-dotnet/pull/446))
- Expose gRPC retry options in Azure Managed extensions ([#447](https://github.com/microsoft/durabletask-dotnet/pull/447))

## v1.11.0

- Add New Property Properties to TaskOrchestrationContext ([#415](https://github.com/microsoft/durabletask-dotnet/pull/415))
- Add automatic retry on gateway timeout in `GrpcDurableTaskClient.WaitForInstanceCompletionAsync` ([#412](https://github.com/microsoft/durabletask-dotnet/pull/412))
- Add specific logging for NotFound error on worker connection ([#413](https://github.com/microsoft/durabletask-dotnet/pull/413))
- Add user agent header to gRPC called ([#417](https://github.com/microsoft/durabletask-dotnet/pull/417))
- Enrich User-Agent Header in gRPC Metadata to indicate Client or Worker as caller ([#421](https://github.com/microsoft/durabletask-dotnet/pull/421))
- Change DTS user agent metadata to avoid overlap with gRPC user agent ([#423](https://github.com/microsoft/durabletask-dotnet/pull/423))
- Add extension methods for registering entities by type ([#427](https://github.com/microsoft/durabletask-dotnet/pull/427))
- Add TaskVersion and utilize it for version overrides when starting orchestrations ([#416](https://github.com/microsoft/durabletask-dotnet/pull/416))
- Update sub-orchestration default versioning ([#437](https://github.com/microsoft/durabletask-dotnet/pull/437))
- Distributed Tracing for Entities (Isolated) ([#404](https://github.com/microsoft/durabletask-dotnet/pull/404))

## v1.10.0

- Update DurableTask.Core to v3.1.0 and Bump version to v1.10.0 by @nytian in ([#411](https://github.com/microsoft/durabletask-dotnet/pull/411))

## v1.9.1

- Add basic orchestration and activity execution logs by @cgillum in ([#405](https://github.com/microsoft/durabletask-dotnet/pull/405))
- Add default version in `TaskOrchestrationContext` by @halspang in ([#408](https://github.com/microsoft/durabletask-dotnet/pull/408))

## v1.9.0

- Fix schedule sample logging setup by @YunchuWang in ([#392](https://github.com/microsoft/durabletask-dotnet/pull/392))
- Introduce versioning to the DurableTaskClient by @halspang in ([#393](https://github.com/microsoft/durabletask-dotnet/pull/393))
- Support for local credential types for DTS by @cgillum in ([#396](https://github.com/microsoft/durabletask-dotnet/pull/396))
- Add utilities for easier versioning usage by @halspang in ([#394](https://github.com/microsoft/durabletask-dotnet/pull/394))
- Add tags to CreateInstanceRequest by @torosent in ([#397](https://github.com/microsoft/durabletask-dotnet/pull/397))
- Partial Purge Support by @YunchuWang in ([#400](https://github.com/microsoft/durabletask-dotnet/pull/400))
- Dts Grpc client retry support by @YunchuWang in ([#403](https://github.com/microsoft/durabletask-dotnet/pull/403))
- Introduce orchestration versioning into worker by @halspang in ([#401](https://github.com/microsoft/durabletask-dotnet/pull/401))

## v1.8.1

- Add timeout to gRPC workitem streaming ([#390](https://github.com/microsoft/durabletask-dotnet/pull/390))

## v1.8.0

- Add Schedule Support for Durable Task Framework ([#368](https://github.com/microsoft/durabletask-dotnet/pull/368))
- Fixes and improvements

## v1.7.0

- Add parent trace context information when scheduling an orchestration ([#358](https://github.com/microsoft/durabletask-dotnet/pull/358))

## v1.6.0

- Added new preview packages, `Microsoft.DurableTask.Client.AzureManaged` and `Microsoft.DurableTask.Worker.AzureManaged`
- Move to Central Package Management ([#373](https://github.com/microsoft/durabletask-dotnet/pull/373))

### Microsoft.DurableTask.Client

- Add new `IDurableTaskClientBuilder AddDurableTaskClient(IServiceCollection, string?)` API

### Microsoft.DurableTask.Worker

- Add new `IDurableTaskWorkerBuilder AddDurableTaskWorker(IServiceCollection, string?)` API
- Add support for work item history streaming

### Microsoft.DurableTask.Worker.Grpc

- Provide entity support for direct grpc connections to DTS ([#369](https://github.com/microsoft/durabletask-dotnet/pull/369))

### Microsoft.DurableTask.Grpc

- Replace submodule for proto files with download script for easier maintenance
- Update to latest proto files

## v1.5.0

- Implement work item completion tokens for standalone worker scenarios ([#359](https://github.com/microsoft/durabletask-dotnet/pull/359))
- Support for worker concurrency configuration ([#359](https://github.com/microsoft/durabletask-dotnet/pull/359))
- Bump System.Text.Json to 6.0.10
- Initial support for the Azure-managed [Durable Task Scheduler](https://techcommunity.microsoft.com/blog/appsonazureblog/announcing-limited-early-access-of-the-durable-task-scheduler-for-azure-durable-/4286526) preview.

## v1.4.0

- Microsoft.Azure.DurableTask.Core dependency increased to `3.0.0`

## v1.3.0

### Microsoft.DurableTask.Abstractions

- Add `RetryPolicy.Handle` property to allow for exception filtering on retries ([#314](https://github.com/microsoft/durabletask-dotnet/pull/314))

## v1.2.4

- Microsoft.Azure.DurableTask.Core dependency increased to `2.17.1`

## v1.2.3

### Microsoft.DurableTask.Client

- Fix filter not being passed along in `PurgeAllInstancesAsync` (https://github.com/microsoft/durabletask-dotnet/pull/289)

### Microsoft.DurableTask.Abstractions

- Enable inner exception detail propagation in `TaskFailureDetails` ([#290](https://github.com/microsoft/durabletask-dotnet/pull/290))
- Microsoft.Azure.DurableTask.Core dependency increased to `2.17.0`

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





