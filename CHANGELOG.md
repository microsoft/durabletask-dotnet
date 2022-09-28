# Changelog

## v0.5.0-beta

### Updates

- Added more API documentation
- Made TaskOrchestrationShim public
- Adds `PurgeInstancesMetadataAsync` and `PurgeInstancesAsync` support and implementation to `DurableTaskGrpcClient`
- Fix issue with mixed Newtonsoft.Json and System.Text.Json serialization.

### Breaking changes

- Added new abstract property `TaskOrchestrationContext.ParentInstance`.
- Added new abstract method `DurableTaskClient.PurgeInstancesAsync`.

## v0.4.1-beta

Initial public release
