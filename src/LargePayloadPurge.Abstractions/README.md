# Large payload purge contracts

`Microsoft.DurableTask.LargePayloadPurge.Abstractions` provides two interfaces in the
`Microsoft.DurableTask.LargePayloadPurge.Abstractions` assembly:

- `Microsoft.DurableTask.AzureBlobPayloads.ILargePayloadPurgeClient` is the activity transport contract for
  fetching tombstones with `GetLargePayloadTombstonesAsync` and reporting outcomes. It accepts a UTC deadline
  and optional cancellation token.
  Transport implementations bind it to the same authenticated task hub as the associated orchestration
  client and preserve the documented gRPC status behavior without requiring this package to reference gRPC.
- `Microsoft.DurableTask.AzureBlobPayloads.IOrchestrationServiceLargePayloadPurgeClient` inherits that shared transport
  contract and adds only `SetLargePayloadAutoPurgeAsync`. The setting operation requires a deadline and
  cancellation token; `DateTime.MaxValue` represents an unspecified deadline. Setting the flag alone does
  not start, stop or wait for a purge runner.

The contract uses the canonical `LargePayloadTombstone`, `LargePayloadPurgeResult`, and
`LargePayloadPurgeDisposition` types from `Microsoft.DurableTask.Client`. It does not copy, move, wrap or
forward those types. Backend-issued tombstone tokens must be echoed unchanged.

## Dependencies and release

This project references the SDK Client project directly. Its packaged dependency graph is:

```text
Microsoft.DurableTask.LargePayloadPurge.Abstractions
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

The assembly uses this repository's strong-name key. Consumers of the unreleased prototype package
`Microsoft.Azure.DurableTask.LargePayloadPurge.Abstractions` must update the package reference and rebuild;
the assembly name is now `Microsoft.DurableTask.LargePayloadPurge.Abstractions`. The shared transport
namespace, `Microsoft.DurableTask.AzureBlobPayloads`, is unchanged. The service capability interface moved
from its prototype `DurableTask.LargePayloadPurge` namespace into that same shared
`Microsoft.DurableTask.AzureBlobPayloads` namespace alongside the transport contract it inherits; this is
a namespace migration for an unreleased prototype type, not a compatibility-preserving move, so there is no
type forwarder for the old namespace. Consumers of the prototype service interface must update their
`using` directives and fully-qualified references and rebuild.
Service implementations should rename `GetLargePayloadsToPurgeAsync` to the inherited
`GetLargePayloadTombstonesAsync`, returning `Task<List<LargePayloadTombstone>>`; Report uses the existing
shared transport signature. No type forwarder or duplicate DTO is provided.
