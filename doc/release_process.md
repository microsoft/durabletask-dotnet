# Release Process

## Overview

| Package prefix | Registry |
|---|---|
| `Microsoft.DurableTask.*` | [NuGet](https://www.nuget.org/profiles/durabletask) |

This repo publishes multiple NuGet packages. Most share a single version defined in `eng/targets/Release.props`. Individual packages can version independently by adding `<VersionPrefix>` and `<VersionSuffix>` properties directly in their `.csproj`.

We follow an approach of releasing everything together, even if a package has no changes — unless we intentionally hold a package back.

`LargePayloadPurge.Abstractions` versions independently in its project file, starting at the planned
`0.1.0` release. It uses source references to the SDK Client models. Publish the corresponding Client and
Abstractions packages containing those models before publishing this package; released Client `1.26.0`
does not contain them. The package uses the repository's MIT license and SDK strong-name key.
Its package and assembly name is `Microsoft.DurableTask.LargePayloadPurge.Abstractions`, covered by the
standard `Microsoft.DurableTask.*.dll` signing pattern. The existing source traversal, SBOM inclusion,
NuGet signing and per-package approval-gated publication steps apply.

Contract publication also waits for successful Client and Abstractions publication. If either prerequisite
fails, including a duplicate-version upload failure, or is skipped or canceled, contract publication is
skipped rather than treating that result as success.

`Microsoft.DurableTask.Extensions.AzureBlobPayloads` depends on the contract package at every target
framework, so its publication job also waits for successful contract publication (in addition to approval).
If contract publication fails for any reason above, including a duplicate-version upload failure, or is
skipped or canceled, Blob publication is skipped rather than treating that result as success. This extends
the same fail-closed chain: approval, then Client and Abstractions, then the contract, then Blob. A
prerequisite that deliberately skips — for example because its exact version is already published — still
skips the dependent job; this is not a general-purpose publication-idempotency mechanism, just the minimum
ordering these two packages require. Other packages retain their own independent publication jobs gated
only on approval.

Keep its `RELEASENOTES.md`: `eng/targets/Release.targets` reads it into NuGet package metadata, whereas the
root `CHANGELOG.md` retains repository release history. The shared target also appends a link using the
package's own version (`releases/tag/v0.1.0` for its initial release). Verify or create the corresponding
release tag before publication; independent package versioning does not create that tag automatically.

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

1. Kick off [ADO release build](https://dev.azure.com/durabletaskframework/Durable%20Task%20Framework%20CI/_build?definitionId=29) (use the tag as the build target, enter `refs/tags/<tag>`)
2. Validate signing, package contents (if build changes were made.)
3. Create ADO release.
    - From successful ADO release build, click 3 dots in top right → 'Release'.
4. Release to ADO feed.
5. Release to NuGet once validated (and dependencies are also released to NuGet.)
6. Publish GitHub draft release.
7. Delete contents of all `RELEASENOTES.md` files.

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
