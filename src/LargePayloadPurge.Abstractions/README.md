# Large payload purge contracts

`Microsoft.Azure.DurableTask.LargePayloadPurge.Abstractions` provides two interfaces in the
`DurableTask.LargePayloadPurge.Abstractions` assembly:

- `DurableTask.LargePayloadPurge.IOrchestrationServiceLargePayloadPurgeClient` is the optional service
  capability for explicit auto-purge state, tombstone fetches, and outcome reports. Each operation requires
  a UTC deadline and cancellation token. `DateTime.MaxValue` represents an unspecified deadline.
  Setting the flag alone does not start or stop a purge runner.
- `Microsoft.DurableTask.AzureBlobPayloads.ILargePayloadPurgeClient` is the activity transport contract for
  fetching tombstones and reporting outcomes. It accepts a UTC deadline and optional cancellation token.
  Transport implementations bind it to the same authenticated task hub as the associated orchestration
  client and preserve the documented gRPC status behavior without requiring this package to reference gRPC.

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
The Azure Blob implementation references this package, not the other way around.

The initial version is planned as `0.1.0`, independently of the repository-wide SDK version. Release the
matching SDK Client and Abstractions dependencies containing the purge models before this package.
Published Client `1.26.0` predates those models and is not sufficient. Repository builds use source project
references; no external Client-version bootstrap property is required.

The assembly uses this repository's strong-name key. Consumers of an earlier local prototype signed with
a different key must rebuild against the SDK-owned package; there is no compatibility promise for those
unreleased prototype binaries. The activity transport interface also moved here from the unreleased
Azure Blob implementation while retaining its full namespace, methods and optional-parameter defaults.
Rebuild consumers against this assembly; no type forwarder is provided.

## License

The service interface originated in [Azure/durabletask](https://github.com/Azure/durabletask) and retains
its Apache-2.0 notice; see [LICENSE](LICENSE). The activity transport interface retains its MIT notice;
see the [SDK MIT license](https://github.com/microsoft/durabletask-dotnet/blob/main/LICENSE). Both license texts
are included in the package as `LICENSE` and `LICENSE-MIT`; the package license expression
is `Apache-2.0 AND MIT`. The referenced SDK model assemblies retain their own licenses.
