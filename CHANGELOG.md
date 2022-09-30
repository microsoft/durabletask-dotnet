# Changelog

## v0.5.0-beta

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
- Adds `PurgeInstancesMetadataAsync` and `PurgeInstancesAsync` support and implementation to `DurableTaskGrpcClient`
- Fix issue with mixed Newtonsoft.Json and System.Text.Json serialization.

### Breaking changes

- Added new abstract property `TaskOrchestrationContext.ParentInstance`.
- Added new abstract method `DurableTaskClient.PurgeInstancesAsync`.

## v0.4.1-beta

Initial public release
