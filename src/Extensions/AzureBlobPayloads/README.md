# Azure Blob payload access scopes

`Microsoft.DurableTask.Extensions.AzureBlobPayloads` externalizes large payloads to
Azure Blob Storage. Uploads always use `LargePayloadStorageOptions.ContainerName`
in the configured storage account. Downloads and deletion share an explicit
destination policy:

| Configuration | Permitted destinations |
| --- | --- |
| Default | The configured storage account endpoint and configured container only. |
| `AllowAllContainersInConfiguredAccount = true` | All containers in the configured account endpoint. |
| `AdditionalAccountUris` | All containers in each explicitly listed additional account endpoint, using the configured identity credential. |

These settings are independent. Listing additional accounts does not broaden
access within the configured account. An entry equivalent to the configured
account is rejected; use `AllowAllContainersInConfiguredAccount` for that account.
To allow every container across primary and additional accounts, set the boolean
and list the additional accounts. Equivalent additional entries are deduplicated.

An account endpoint includes its scheme, hostname, effective port, and any
Azure SDK path-style account component, such as `accounta` in
`https://127.0.0.1:10000/accounta`. An account-wide scope relaxes only the container
restriction: it does not permit another scheme, port, or path-style account.
The SDK uses `BlobUriBuilder` for DNS-style and path-style URLs and validates
the final normalized client URI before storage requests or credential acquisition.
Blob-name escaping and literal leading or trailing slashes are preserved:
`blob`, `/blob`, and `blob/` identify distinct objects for reads and deletion.
A blob name that normalizes outside its selected container is rejected.
The final decoded blob name must also match the reference's decoded name exactly.
If client construction changes that name, for example by resolving internal dot
segments, the reference is rejected before Storage I/O or credential acquisition
rather than selecting another object in the same container. Such malformed
references use the existing purge quarantine classification, which preserves the
tombstone but requires operator action. Scope-denied references remain retryable.

There is no hostname-only allowlist and no permission to follow arbitrary accounts
merely because the configured identity has Storage permissions.

## Configuration

Use the existing host-wide payload options. The default remains restrictive:

```csharp
services.AddExternalizedPayloadStore(options =>
{
    options.AccountUri = new Uri(
        Environment.GetEnvironmentVariable("DURABLETASK_STORAGE_ACCOUNT_URI")
        ?? throw new InvalidOperationException("DURABLETASK_STORAGE_ACCOUNT_URI is not set."));
    options.Credential = new DefaultAzureCredential();
    options.ContainerName = "durabletask-payloads";
});
```

Client and worker builders can then call `UseExternalizedPayloads()` to share the
store. The callback overloads of `UseExternalizedPayloads` accept the same options.
The store is a host-wide singleton, not a separate store per named client or worker:
register the shared store first or ensure the first builder's configuration is the
configuration intended for the whole host.

### Explicitly broader scopes

Only when the application intends to resolve or delete payloads throughout the
configured account, set:

```csharp
options.AllowAllContainersInConfiguredAccount = true;
```

To authorize all containers in a specific additional account, configure identity
authentication and add that account's endpoint:

```csharp
options.AdditionalAccountUris.Add(new Uri(
    Environment.GetEnvironmentVariable("DURABLETASK_ADDITIONAL_STORAGE_ACCOUNT_URI")
    ?? throw new InvalidOperationException("DURABLETASK_ADDITIONAL_STORAGE_ACCOUNT_URI is not set.")));
```

Each additional entry must be an absolute HTTPS **account** endpoint, not a
container or blob URL. User information, query strings (including SAS), and
fragments are not accepted. The endpoints and account-wide switch are captured
when the store is constructed; recreate the store or restart the host to apply
changes. Broader scopes allow incoming references to select blobs throughout
those accounts under the configured Storage identity. Storage permissions still
apply and should reflect the intended application access.

## Authentication

| Mode | Configured container | Other containers in configured account | Additional accounts |
| --- | --- | --- | --- |
| `AccountUri` + `TokenCredential` | Supported | Explicit account-wide opt-in; same credential | Explicit endpoint entries; same credential; HTTPS required |
| Connection string with account key | Supported | Explicit account-wide opt-in; existing account-key pipeline | Not supported |
| Connection string with SAS | Supported within SAS permissions | Explicit account-wide opt-in; existing SAS permissions still apply | Not supported |

When both a complete identity configuration (`AccountUri` and `Credential`) and a
connection string are supplied, identity authentication continues to take
precedence. A credential without `AccountUri` does not change a connection-string
configuration into identity authentication. Additional accounts without a complete
identity configuration fail during store construction.

The SDK never substitutes credentials, falls back from identity to a connection
string, or uses SAS credentials supplied in a payload URL. Same-account operations
retain the configured authentication pipeline. Additional-account clients use
the configured `TokenCredential` and the store's retry options, without inheriting
a primary-account secondary-host route. The default Azure SDK pipeline and
transport do not follow redirects. Storage permission
failures, including a container-scoped SAS attempting another container, are
surfaced to the caller rather than retried under a different authentication mode.

## Tokens and migration

Token formats do not change:

- New uploads still produce `blob:v2:<absolute-blob-URL>`, with user information,
  query strings (including configured SAS), and fragments removed.
- Legacy `blob:v1:<container>:<blobName>` references remain readable and deletable
  by `BlobPayloadStore`; their account is implicitly the configured account.
  Account-wide opt-in can authorize other containers named by v1 references.
- The auto-purge protocol continues to defer unsupported versions and quarantine
  legacy v1 tombstones, which lack an explicit account. This is separate from the
  store's direct v1 compatibility.

Knowing a blob's URL does not authorize access. Token user information is rejected.
Legacy v2 URLs containing SAS or other query parameters remain readable, but
queries and fragments are discarded before destination parsing: they never
supply authentication or select snapshots or versions. Only the configured SAS
or identity is used. Literal `#` and `?` characters in v2 blob names must be
URL-escaped as `%23` and `%3F`. Older hand-authored tokens relying on unescaped
fragment text as part of a name must be rewritten with that text escaped.
Before upgrading, complete affected instances or migrate payloads and references
whose decoded blob names contain standalone `.` or `..` segments to names that
do not change during URI normalization. SDK-generated GUID blob names are
unaffected.

Before changing the configured account or container, complete or explicitly
migrate existing instances and clean up their payloads. Alternatively, deliberately
authorize the old location: use the configured-account switch for another
container in the same account, or an additional-account entry for a different
account. The latter requires identity authentication with access to that account
and grants the scope for all its containers, not just the old payload container.
V1 references cannot identify an old account after an account change.

Removing a scope makes existing references outside the remaining scopes fail.
Deletion uses the same policy as downloads. Disallowed v2 purge references remain
retryable and their tombstones are not reported as successfully deleted; restoring
the appropriate explicit scope allows a later attempt to proceed. An authorized
delete still checks the blob's ownership metadata and uses its ETag in the delete
condition.

## SDK and dashboard boundaries

The client and worker gRPC interceptors resolve orchestration inputs, external
events, activity and orchestration results, history chunks, status fields, and
entity payloads through `PayloadStore.DownloadAsync`. `BlobPayloadStore` enforces
the same policy there and in direct `DeleteAsync` calls and auto-purge activities.
Uploads are unaffected by broader read/delete scopes.

Custom `PayloadStore` implementations own their own access policy; these options
govern `BlobPayloadStore`. This package does not implement a Storage proxy service
or a dashboard. Dashboard reads using the signed-in user's delegated Storage
identity and independent Storage permissions are outside this store's routing
policy; no worker/service-credential fallback is introduced.
