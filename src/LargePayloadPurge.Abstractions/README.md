# Large payload purge service abstraction

`Microsoft.Azure.DurableTask.LargePayloadPurge.Abstractions` provides the optional
`DurableTask.LargePayloadPurge.IOrchestrationServiceLargePayloadPurgeClient` capability for orchestration
service adapters. The assembly is `DurableTask.LargePayloadPurge.Abstractions`; its only public type is
the interface.

The three operations set explicit auto-purge state, fetch tombstones, and report outcomes. Each accepts a
UTC deadline and cancellation token. `DateTime.MaxValue` represents an unspecified deadline. Setting the
flag alone does not start or stop a purge runner.

The contract uses the canonical `LargePayloadTombstone`, `LargePayloadPurgeResult`, and
`LargePayloadPurgeDisposition` types from `Microsoft.DurableTask.Client`. It does not copy, move, wrap or
forward those types. Backend-issued tombstone tokens must be echoed unchanged.

## Dependencies and release

This project references the SDK Client project directly. Its packaged dependency graph is:

```text
Microsoft.Azure.DurableTask.LargePayloadPurge.Abstractions
  -> Microsoft.DurableTask.Client
    -> Microsoft.DurableTask.Abstractions
      -> Microsoft.Azure.DurableTask.Core
```

It is not a BCL-only package. Core does not depend on this package or the SDK Client. The contract package
contains no blob storage, gRPC transport, worker, or orchestration implementation.

The initial version is planned as `0.1.0`, independently of the repository-wide SDK version. Release the
matching SDK Client and Abstractions dependencies containing the purge models before this package.
Published Client `1.26.0` predates those models and is not sufficient. Repository builds use source project
references; no external Client-version bootstrap property is required.

The assembly uses this repository's strong-name key. Consumers of an earlier local prototype signed with
a different key must rebuild against the SDK-owned package; there is no compatibility promise for those
unreleased prototype binaries.

## License

The interface originated in [Azure/durabletask](https://github.com/Azure/durabletask) and retains its
Apache-2.0 license and copyright notice. See [LICENSE](LICENSE). The referenced SDK model assemblies
retain their own licenses.
