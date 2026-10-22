# Release Process

## Overview

| Package prefix | Registry |
|---|---|
| `Microsoft.DurableTask.*` | [NuGet](https://www.nuget.org/profiles/durabletask) |

This repo publishes multiple NuGet packages. Most share a single version defined in `eng/targets/Release.props`. Individual packages can version independently by adding `<VersionPrefix>` and `<VersionSuffix>` properties directly in their `.csproj`.

We follow an approach of releasing everything together, even if a package has no changes — unless we intentionally hold a package back.

`LargePayloadPurge.Abstractions` versions with the shared repository-wide `VersionPrefix`/`VersionSuffix` in
`eng/targets/Release.props`, the same as Client, Abstractions, and most other `Microsoft.DurableTask.*`
packages (a few, such as `Generators`, deliberately keep their own independent version); it has no
package-local version override. It uses source references to the SDK Client models, so publish it in the
same release as the Client and Abstractions packages that contain those models; released Client `1.26.0`
predates them and is not sufficient on its own until a release that includes them is published. The package
uses the repository's MIT license and SDK strong-name key.
Its package and assembly name is `Microsoft.DurableTask.LargePayloadPurge.Abstractions`, covered by the
standard `Microsoft.DurableTask.*.dll` signing pattern. The existing source traversal, SBOM inclusion,
NuGet signing and per-package approval-gated publication steps apply.

Contract publication also waits for successful Client and Abstractions jobs. Each prerequisite must
either publish successfully or complete an explicit successful no-op because the operator confirmed its
selected artifact version is already on NuGet.org using `alreadyPublishedPackages`. If either job fails,
including a duplicate-version upload failure, or is intentionally held back, skipped or canceled, contract
publication is skipped rather than treating that result as success.

`Microsoft.DurableTask.Extensions.AzureBlobPayloads` depends on the contract package at every target
framework, so its publication job also waits for successful contract publication (in addition to approval).
If the contract job fails for any reason above, including a duplicate-version upload failure, or is
intentionally held back, skipped or canceled, Blob publication is skipped rather than treating that result
as success. This extends the same fail-closed chain: approval, then Client and Abstractions, then the
contract, then Blob. An already-published confirmation succeeds without staging or submitting that
package, allowing recovery of a partial release: confirm Client/Abstractions to retry the contract and
Blob. For a Blob-only retry, confirm all selected versions already published, including Client,
Abstractions and the contract; their jobs still follow the dependency chain but complete as no-ops.
Intentional holds belong in `skipPackages` and still block dependents. If a package appears in both
lists, the hold takes precedence; it is not permission to publish dependents. Both lists default to
empty and apply only to the selected run. Other packages retain their own independent publication
jobs gated only on approval.

Keep its `RELEASENOTES.md`: `eng/targets/Release.targets` reads it into NuGet package metadata, whereas the
root `CHANGELOG.md` retains repository release history. The shared target also appends a link using the
package's own version, which is now the shared repository-wide version, so the **Prepare Release**
workflow's existing `release/vX.Y.Z` branch and `vX.Y.Z` tag cover this package automatically — no separate
version or tag step is needed for it.

### Cross-repository release order for the blob auto-purge integration

This feature spans independently released repositories. Each downstream repository's merge and release
depends on the previous one having an actual **published** (not local or session-only) compatible package,
in this order:

1. **Core** publishes a compatible `Microsoft.Azure.DurableTask.Core` release first.
2. **This repo** (`durabletask-dotnet`) then releases actual new SDK `Client`/`Abstractions` packages (and
   `Grpc`/`Worker` as needed) against that published Core, before the `LargePayloadPurge.Abstractions`
   contract and `Extensions.AzureBlobPayloads` packages. Since the contract now shares the same repository-wide
   version as `Client`/`Abstractions` (it has no independent version of its own), all of these publish from
   the same release run; "before" here means publish job order within that run, not a separate version or a
   later release. The existing publication-pipeline `dependsOn` gates above enforce only `nugetApproval` →
   `Client`/`Abstractions` → the contract → Blob; they do not gate Core publication, and they do not gate
   every package in the SDK's actual nuspec dependency closure (for example `Grpc` or `Worker`, which
   `Extensions.AzureBlobPayloads` also depends on but whose release jobs are not inputs to the contract or
   Blob gate). Before treating this step as complete, or merging a downstream repository against it, a
   release operator must separately confirm that the SDK's entire actual nuspec dependency closure is
   published and restorable, not only the three packages the pipeline gates on. The contract's actual first
   release uses whichever shared SDK version is approved and published next; do not assume or pin to a
   specific future version, and do not treat the existing published `1.26.0` line as already containing it.
3. **Durable Functions** must pin the actual published compatible Core/SDK/contract versions, not local or
   session-only ones, before merging and releasing its host and optional packages.
4. The private AzureManaged provider releases last, after the Durable Functions host and this repo's SDK
   are published, with its own committed dependency-version upgrade and a clean restore and test pass
   against the published packages.

This order reduces, but does not eliminate, the risk of a downstream repository depending on an unpublished
or incompatible upstream version. It does not substitute for verifying that every repository's committed
package references are already pinned to real published versions; do not invent release versions or bump a
committed dependency pin to a local or session-only one to make this order appear satisfied. In particular,
do not assume a previously published SDK version already contains these new types merely because its number
precedes an unreleased one — confirm against the actual release notes or package contents.

### Versioning Scheme

We follow [semver](https://semver.org/) with optional pre-release tags:

```
X.Y.Z-preview.N  →  X.Y.Z-rc.N  →  X.Y.Z (stable)
```

## Automated Release Preparation (Recommended)

Use the **Prepare Release** GitHub Action to automate the release preparation process.

### Running the Workflow

1. Go to **Actions** → **Prepare Release** in GitHub
2. Click **Run workflow**
3. Optionally specify a version (e.g., `1.24.0` or `1.24.0-preview.1`). Leave empty to auto-increment (patch for stable, pre-release number for pre-release).
4. Click **Run workflow**

### What the Workflow Does

1. **Determines the next version**: If not specified, auto-increments the current version from `Release.props`
2. **Generates changelog**: Uses `git log` to find changes between `main` and the last release tag
3. **Updates `Release.props`**: Bumps `VersionPrefix` and `VersionSuffix` to the new version
4. **Updates `CHANGELOG.md`**: Adds a new version section with the discovered changes
5. **Creates a release branch**: `release/vX.Y.Z`
6. **Creates a release tag**: `vX.Y.Z`

### After the Workflow Completes

The workflow summary will include a link to create a PR. You must **manually create a pull request** from the release branch (`release/vX.Y.Z`) to `main`:

1. Go to the workflow run summary and click the **Create PR** link, or navigate to: `https://github.com/microsoft/durabletask-dotnet/compare/main...release/vX.Y.Z`
2. Set the PR title to `Release vX.Y.Z`
3. Review the version bump in `eng/targets/Release.props` and changelog updates
4. Update per-package `RELEASENOTES.md` files if needed
5. Merge the PR after CI passes

After the PR is merged, follow the **Publishing** steps below.

## Publishing (After Release PR is Merged)

1. Select a successful, signed `durabletask-dotnet.official` build in the `azfunc/internal` project.
   Reuse the approved build's `drop`; the publisher does not rebuild, repack, or change package versions.
2. Validate the selected build's source commit, signatures, package contents, and exact versions.
3. Use [`durabletask-dotnet.publish`](https://dev.azure.com/azfunc/internal/_build?definitionId=1100)
   (`eng/publish/publish.yml`). The publisher source ref and the `officialPipeline` artifact resource
   are separate selections: explicitly pin the official run, not just its branch or "latest".
   Normally use the reviewed publisher-source commit on `main` after the code-mirror pipeline has
   mirrored it to ADO. The branch allowlist controls automatic mirror triggers; it does not rule out
   a separately authorized manual invocation of the existing mirror workflow for an exact trial ref.
   Do not expand the trigger allowlist or change mirror policies for a trial. Selecting the old
   release tag as the publisher source loads the old publisher YAML, even when the correct signed
   official artifact run is selected.
4. Supply `nuGetOrganization`, the NuGet.org organization confirmed during ESRP NuGet onboarding.
   There is no organization default; an unset or unresolved value fails before any ESRP submission.
   Set `alreadyPublishedPackages` to exact public package IDs only after verifying their **selected
   artifact versions** already exist on NuGet.org. Those jobs log a successful no-op, without package
   staging or an ESRP task, so dependent jobs can proceed.
   Use `skipPackages` only for intentional holds: those jobs are skipped and still block dependents.
   A hold takes precedence if a package is in both lists. These are per-run inputs, not permanent removals.
5. The publisher first stages the signed packages to the existing internal ADO feed. Only after that
   succeeds does `nugetApproval` wait for manual approval. The gate displays the selected organization
   and both package lists, and requires confirmation of NuGet onboarding, publisher allowlisting,
   Trusted Publishing and each exact already-published version.
   Do not approve until those prerequisites, the selected artifacts, both lists and dependency
   availability have been checked. Stop at the gate for a validation-only trial.
   Rejecting or timing out the gate prevents all public publication.
6. After approval, each public package not held or already confirmed published is copied unchanged into
   its own ESRP folder using the exact nuspec ID, then submitted with `EsrpRelease@14` and
   `WaitForReleaseCompletion: true`.
   Internal-only/test packages and symbol packages are not submitted. Missing or multiple matching
   packages fail explicitly. Each package retains its independent job and the dependency gates above;
   ESRP does not guarantee ordering within a multi-package submission.
7. Publish GitHub draft release.
8. Delete contents of all `RELEASENOTES.md` files.

### ESRP NuGet prerequisites

The [ESRP NuGet guide](https://eng.ms/docs/microsoft-security/identity/trust-and-security-services/tss-release-distribute/tss-release-esrp-parent/oss-publishing/releasing-open-source/nuget)
requires channel onboarding and a NuGet.org Trusted Publishing policy for the customer's organization,
ESRP client identity, package IDs or delegated prefixes, and required operations (new IDs and/or new
versions). Confirm organization ownership and any collaborator access required by that onboarding.
Existing ESRP customers use the existing-customer onboarding process to enable the NuGet channel.

The template reuses the Durable ESRP service connection `dtfx-internal-esrp-prod`, managed identity
`0b3ed1a4-0727-4a50-b82a-02c2bd9dec89`, tenant `33e01921-4d64-4f8c-a055-5bdaffd5e33d`,
vault `durable-esrp-akv`, and signing certificate `dts-esrp-cert`. NuGet distribution uses
`MainPublisher: ESRPRELPACMAN`, not the `durabletask-java` publisher used for other registries.
The client must be allowlisted for this publisher. `Owners` and `Approvers` use the individual
Microsoft identity of the person who queued the run (`Build.RequestedForEmail`), not a group alias.
The publishing pipeline must also be authorized to use this service connection; that ADO authorization
is separate from ESRP publisher allowlisting and NuGet Trusted Publishing permissions.

**Durable-specific NuGet onboarding, organization choice, publisher allowlisting, and Trusted Publishing
permissions have not been verified by this source migration.** Package owner names alone do not prove
that either owner is the onboarded organization. Confirm these prerequisites with the ESRP/NuGet teams
before approving publication. A failed ESRP submission fails the job; there is no direct NuGet push fallback.

### v1.27.1 trial selection

The signed official drop has 18 `.nupkg` files: 14 public stable packages at `1.27.1`,
`ExportHistory` and `ScheduledTasks` at `1.27.1-preview.2`, the unchanged public
`Microsoft.DurableTask.Generators` at `2.1.0-preview.2`, and internal-only
`Microsoft.DurableTask.InProcessTestHost` at `0.2.3-preview.1`.
For this trial, set `alreadyPublishedPackages: [Microsoft.DurableTask.Generators]` and
`skipPackages: []` because that exact generator version is already public. Its job succeeds through a
logged no-op, not an ESRP submission. This leaves 16 public submission targets; the test host has no
public job. Both default lists are empty so future generator releases remain enabled.
Keep `officialPipeline` pinned to the approved signed run `311120` (build number `20261021.2`,
source `2a409da4ac7dcf03c4f25985337eb4623ea6394e`), while the publisher uses the new reviewed and mirrored
source containing this migration (normally the reviewed `main` commit). A run ID and build number
are not interchangeable.
Recheck exact-version absence for every intended target immediately before any submission;
search indexing alone is not proof that a version has never been published.

#### Pinned trial request and queue permissions

The pipeline's `skipPackages` and `alreadyPublishedPackages` parameters are YAML/UI lists. For the
trial, use an empty hold list and `[Microsoft.DurableTask.Generators]` as the already-published list.
For the verified REST request through the installed Azure DevOps
CLI SDK, however, `RunPipelineParameters.template_parameters` is typed as `{str}`: send
`templateParameters` as a map of strings, with the list serialized as JSON **inside a string**.
This request fragment illustrates the wire value; `<confirmed-org>` is a placeholder, not a default:

```json
{
  "templateParameters": {
    "nuGetOrganization": "<confirmed-org>",
    "skipPackages": "[]",
    "alreadyPublishedPackages": "[\"Microsoft.DurableTask.Generators\"]"
  }
}
```

Do not send either list as a native JSON array through that client. In the earlier trial, that request
invalidated or ignored overrides and resolved default source/artifact selections instead.
Verify the resolved publisher source, official artifact run, organization bindings and exclusion
conditions in the actual expanded preview before attempting a run; an HTTP 200 alone is insufficient.

Selecting nondefault pinned trial inputs requires **Edit queue build configuration** on
`internal` project pipeline `1100` (`durabletask-dotnet.publish`): Build security action
`EditPipelineQueueConfigurationPermission` (bit `65536`), distinct from **Queue builds** (bit `128`)
and **Edit build pipeline** (bit `2048`). If Azure DevOps returns
`TF215106` requesting that permission, stop and have an ADO administrator grant that narrow permission
to the intended requester before another attempt. Do not change pipeline defaults, switch
credentials/APIs, or loosen policies to bypass the denial. This queue-configuration permission is
separate from authorization to use `dtfx-internal-esrp-prod`, ESRP `ESRPRELPACMAN` allowlisting,
NuGet channel onboarding and Trusted Publishing.

#### Observed trial status before partial-release recovery fix (2026-10-22)

The tested runtime source is `261b233a828211dae2439afba6a9cf3601d79c5c`. Mirror run `311345`
succeeded and its locked internal ref matched that commit. The actual-source publisher preview
returned HTTP 200; scalar positive-control and corrected string-map previews confirmed all 17
organization/environment bindings, the visible selected-organization/STOP approval instructions,
the Generator job condition set to `False`, and the remaining 16 runnable jobs with dependency-success
gates preserved. The organization used for compilation was a candidate, not verified onboarding.

An earlier malformed array-valued request created run `311347` with default old `main` source
`2a409da4`, latest official run `311261`, and empty template parameters. It was canceled before any
manual approval; it is not evidence of a correctly pinned trial.
With the corrected request, official build `20261021.2`, and fresh exact-version HTTP 404s for all
16 targets, the queue attempt returned HTTP 500 / `TF215106` before assigning a run ID:
the requester lacked **Edit queue build configuration** on pipeline `1100`.
This is a hard ADO authorization blocker, not a source compilation failure or an observed
ESRP/backend/NuGet failure. No permission grant, policy change, approval or public distribution was
performed. Post-denial run inventory confirmed that the corrected request created no run and that
`311347` was completed/canceled; no accidental publisher remained active.
Later documentation-only commits did not replace `261b233a` as the exact runtime tested in that preview.
The subsequent source change adds `alreadyPublishedPackages` to distinguish successful no-ops from
intentional holds. It is not covered by that earlier server preview; the final recovery/holdback variants
need their own actual-source compilation before use. The queue-configuration and downstream onboarding
prerequisites remain separate and unresolved; no trial parameter change bypasses them.

For source-only validation, PowerShell 7 with the `Pester` (5.x) and `powershell-yaml` modules can run:

```powershell
Invoke-Pester -Path .\eng\publish\publish.Tests.ps1 -CI
```

These checks exercise local staging and pipeline structure only. They do not compile the server-side
1ES templates, validate backend permissions, queue a release, or publish a package.

## Manual Release Process (Alternative)

If you prefer to prepare the release manually, follow these steps:

1. Rev versions as appropriate following semver.
    - Repo-wide versions can be found in `eng/targets/Release.props`
    - Individual packages can version independently by adding the `<VersionPrefix>` and `<VersionSuffix>` properties directly in their `.csproj`.
2. Ensure appropriate `RELEASENOTES.md` are updated in the repository.
    - These are per-package and contain only that packages changes.
    - The contents will be included in the `.nupkg` and show up in nuget.org's release notes tab.
    - Clear these files after a release.
3. Ensure `CHANGELOG.md` in repo root is updated.
4. Tag the release.
    - `git tag v<version>`, `git push <remote> -u <tag>`
5. Draft GitHub release for new tag.
6. Follow the **Publishing** steps above.
