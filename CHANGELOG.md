# Changelog

## v0.5.0-beta

- Adds `PurgeInstancesMetadataAsync` nad `PurgeInstancesAsync` support and implementation to `DurableTaskGrpcClient`

### Breaking changes

- Added new abstract property `TaskOrchestrationContext.ParentInstance`.
- Added new abstract method `DurableTaskClient.PurgeInstancesAsync`.

## v0.4.1-beta

Initial public release
