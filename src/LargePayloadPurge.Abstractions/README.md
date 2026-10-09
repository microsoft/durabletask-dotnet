# Large payload purge contracts

`Microsoft.DurableTask.LargePayloadPurge.Abstractions` provides two interfaces in the
`Microsoft.DurableTask.LargePayloadPurge.Abstractions` assembly:

- `Microsoft.DurableTask.LargePayloadPurge.Abstractions.ILargePayloadPurgeClient` is the activity transport contract for
  fetching tombstones with `GetLargePayloadTombstonesAsync` and reporting outcomes. It accepts a UTC deadline
  and optional cancellation token.
  Transport implementations bind it to the same authenticated task hub as the associated orchestration
  client and preserve the documented gRPC status behavior without requiring this package to reference gRPC.
- `Microsoft.DurableTask.LargePayloadPurge.Abstractions.IOrchestrationServiceLargePayloadPurgeClient` inherits that shared transport
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

This package follows the repository's shared release version, defined once in `eng/targets/Release.props`,
the same as Client, Abstractions, and most other `Microsoft.DurableTask.*` packages (a few packages, such as
`Generators`, deliberately keep their own independent version) — it has no package-local version override of
its own. Release the matching SDK Client and Abstractions packages containing the purge models alongside this
one; published Client `1.26.0` predates those models and is not sufficient. The **Prepare Release** workflow
(see `doc/release_process.md`) already bumps the shared version for every package that follows it, including
this one, with no separate step required. Repository builds use source project references; no external
Client-version bootstrap property is required.

The assembly uses this repository's strong-name key. Consumers of the unreleased prototype package
`Microsoft.Azure.DurableTask.LargePayloadPurge.Abstractions` must update the package reference and rebuild;
the assembly name is now `Microsoft.DurableTask.LargePayloadPurge.Abstractions`. Both interfaces now live in
that same `Microsoft.DurableTask.LargePayloadPurge.Abstractions` namespace, matching the package's own name
rather than reusing the Azure Blob extension's `Microsoft.DurableTask.AzureBlobPayloads` implementation
namespace: this contracts package is backend-neutral and does not depend on blob storage, so it should not
appear to be owned by one specific backend implementation's namespace. This is a namespace migration for
unreleased prototype types, not a compatibility-preserving move, so there is no type forwarder from either
prior location — the transport interface's original `Microsoft.DurableTask.AzureBlobPayloads` namespace (its
home since this contracts package was first split out of the Blob implementation) or the service interface's
`DurableTask.LargePayloadPurge` Core-prototype namespace. Consumers of either prototype interface must update
their `using` directives and fully-qualified references and rebuild. The Azure Blob implementation itself
keeps its own `Microsoft.DurableTask.AzureBlobPayloads` namespace unchanged for its actual implementation
types (`BlobPurgeJobOrchestrator`, the generated-client adapter, and the purge activities); it now references
this package's contracts namespace explicitly via `using`, the same as any other consumer would.
Service implementations should rename `GetLargePayloadsToPurgeAsync` to the inherited
`GetLargePayloadTombstonesAsync`, returning `Task<List<LargePayloadTombstone>>`; Report uses the existing
shared transport signature. No type forwarder or duplicate DTO is provided.
