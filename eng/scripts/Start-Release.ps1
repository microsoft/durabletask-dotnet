# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

<#
.SYNOPSIS
    Bumps package versions, generates changelog, creates a release branch, and opens a PR.

.DESCRIPTION
    This script automates the SDK release kickoff process:
    1. Bumps the version in eng/targets/Release.props based on the bump type or explicit version.
    2. Runs generate_changelog.py to produce a changelog entry.
    3. Prepends the generated changelog entry to CHANGELOG.md.
    4. Creates a release branch and pushes it.
    5. Opens a pull request targeting main.

.PARAMETER BumpType
    The type of version bump: 'major', 'minor', 'patch', or 'explicit'.

.PARAMETER ExplicitVersion
    The explicit version to set (required when BumpType is 'explicit'). Format: 'X.Y.Z'.

.PARAMETER VersionSuffix
    Optional pre-release suffix (e.g., 'preview', 'rc.1'). Leave empty for stable releases.
#>

param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('major', 'minor', 'patch', 'explicit')]
    [string]$BumpType,

    [Parameter(Mandatory = $false)]
    [string]$ExplicitVersion = '',

    [Parameter(Mandatory = $false)]
    [string]$VersionSuffix = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path "$PSScriptRoot/../..").Path
$releasePropsPath = Join-Path $repoRoot 'eng/targets/Release.props'
$changelogPath = Join-Path $repoRoot 'CHANGELOG.md'
$changelogScript = Join-Path $repoRoot 'generate_changelog.py'

function Get-CurrentVersion {
    [xml]$props = Get-Content $releasePropsPath -Raw
    $versionPrefix = $props.Project.PropertyGroup.VersionPrefix
    if (-not $versionPrefix) {
        throw "Could not find VersionPrefix in $releasePropsPath"
    }
    return $versionPrefix.Trim()
}

function Get-BumpedVersion {
    param(
        [string]$CurrentVersion,
        [string]$BumpType,
        [string]$ExplicitVersion
    )

    if ($BumpType -eq 'explicit') {
        if (-not $ExplicitVersion) {
            throw "ExplicitVersion is required when BumpType is 'explicit'."
        }
        if ($ExplicitVersion -notmatch '^\d+\.\d+\.\d+$') {
            throw "ExplicitVersion must be in the format 'X.Y.Z'. Got: '$ExplicitVersion'"
        }
        return $ExplicitVersion
    }

    $parts = $CurrentVersion.Split('.')
    if ($parts.Count -ne 3) {
        throw "Current version '$CurrentVersion' is not in expected X.Y.Z format."
    }

    [int]$major = $parts[0]
    [int]$minor = $parts[1]
    [int]$patch = $parts[2]

    switch ($BumpType) {
        'major' { $major++; $minor = 0; $patch = 0 }
        'minor' { $minor++; $patch = 0 }
        'patch' { $patch++ }
    }

    return "$major.$minor.$patch"
}

function Set-Version {
    param(
        [string]$NewVersion,
        [string]$Suffix
    )

    $content = Get-Content $releasePropsPath -Raw

    # Update VersionPrefix
    $content = $content -replace '<VersionPrefix>[^<]*</VersionPrefix>', "<VersionPrefix>$NewVersion</VersionPrefix>"

    # Update VersionSuffix
    $content = $content -replace '<VersionSuffix>[^<]*</VersionSuffix>', "<VersionSuffix>$Suffix</VersionSuffix>"

    Set-Content -Path $releasePropsPath -Value $content -NoNewline -Encoding UTF8
    Write-Host "Updated $releasePropsPath -> VersionPrefix=$NewVersion, VersionSuffix=$Suffix"
}

function Update-Changelog {
    param(
        [string]$VersionTag
    )

    Write-Host "Generating changelog for tag 'v$VersionTag'..."

    # Run the changelog generator with v-prefixed tag to match repo tagging convention
    $generatorOutput = & python $changelogScript --tag "v$VersionTag" 2>&1 | Out-String
    $generatorSucceeded = $LASTEXITCODE -eq 0
    if (-not $generatorSucceeded) {
        Write-Warning "Changelog generation returned non-zero exit code. Output: $generatorOutput"
    }

    # Extract changelog entries (lines starting with "- ") from generator output
    $generatedEntries = ''
    if ($generatorSucceeded -and $generatorOutput) {
        $entryLines = $generatorOutput -split "`n" | Where-Object { $_ -match '^- ' }
        $generatedEntries = ($entryLines -join "`n").Trim()
    }

    # Read the existing changelog
    $existingChangelog = Get-Content $changelogPath -Raw

    # Replace "## Unreleased" section with the new version entry
    if ($existingChangelog -match '(?s)(## Unreleased\r?\n)(.*?)((?=\r?\n## )|$)') {
        $unreleasedContent = $Matches[2].Trim()

        # Use generated entries if available, fall back to existing unreleased content
        $versionContent = if ($generatedEntries) { $generatedEntries } else { $unreleasedContent }

        $newSection = "## Unreleased`n`n## v$VersionTag`n"
        if ($versionContent) {
            $newSection = "## Unreleased`n`n## v$VersionTag`n`n$versionContent`n"
        }
        $updatedChangelog = $existingChangelog -replace '(?s)## Unreleased\r?\n.*?(?=(\r?\n## )|$)', $newSection
        Set-Content -Path $changelogPath -Value $updatedChangelog -NoNewline -Encoding UTF8
        Write-Host "Updated CHANGELOG.md with version v$VersionTag"
    }
    else {
        Write-Warning "Could not find '## Unreleased' section in CHANGELOG.md. Skipping changelog update."
    }
}

# --- Main Script ---

Write-Host "=== SDK Release Kickoff ==="
Write-Host ""

# Step 1: Compute new version
$currentVersion = Get-CurrentVersion
Write-Host "Current version: $currentVersion"

$newVersion = Get-BumpedVersion -CurrentVersion $currentVersion -BumpType $BumpType -ExplicitVersion $ExplicitVersion
Write-Host "New version: $newVersion"

$fullVersion = $newVersion
if ($VersionSuffix) {
    $fullVersion = "$newVersion-$VersionSuffix"
}
Write-Host "Full version: $fullVersion"

# Step 2: Create release branch
$branchName = "release/v$fullVersion"
Write-Host ""
Write-Host "Creating release branch: $branchName"

Push-Location $repoRoot
try {
    # Ensure we start from an up-to-date main branch
    Write-Host "Switching to main and pulling latest..."
    git checkout main
    if ($LASTEXITCODE -ne 0) { throw "Failed to checkout main." }
    git pull origin main
    if ($LASTEXITCODE -ne 0) { throw "Failed to pull latest main." }

    git checkout -b $branchName
    if ($LASTEXITCODE -ne 0) { throw "Failed to create branch '$branchName'." }

    # Step 3: Bump version in Release.props
    Set-Version -NewVersion $newVersion -Suffix $VersionSuffix

    # Step 4: Update CHANGELOG.md
    Update-Changelog -VersionTag $fullVersion

    # Step 5: Commit and push
    git add eng/targets/Release.props
    git add CHANGELOG.md
    git commit -m "Bump version to $fullVersion and update changelog"
    if ($LASTEXITCODE -ne 0) { throw "Failed to commit changes." }

    git push origin $branchName
    if ($LASTEXITCODE -ne 0) { throw "Failed to push branch '$branchName'." }

    Write-Host ""
    Write-Host "Release branch '$branchName' pushed successfully."

    # Step 6: Open a PR using the GitHub CLI
    $prTitle = "Release v$fullVersion"
    $prBody = @"
## SDK Release v$fullVersion

This PR prepares the release of **v$fullVersion**.

### Changes
- Bumped ``VersionPrefix`` to ``$newVersion`` in ``eng/targets/Release.props``
- Updated ``VersionSuffix`` to ``$VersionSuffix``
- Updated ``CHANGELOG.md``

### Release Checklist
- [ ] Verify version bump is correct
- [ ] Verify CHANGELOG.md entries are accurate
- [ ] Verify RELEASENOTES.md files are updated (if needed)
- [ ] Merge this PR
- [ ] Tag the release: ``git tag v$fullVersion``
- [ ] Kick off the [ADO release build](https://dev.azure.com/durabletaskframework/Durable%20Task%20Framework%20CI/_build?definitionId=29) targeting ``refs/tags/v$fullVersion``
"@

    Write-Host "Opening pull request..."
    gh pr create --base main --head $branchName --title $prTitle --body $prBody
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Failed to create PR via 'gh'. You can create it manually at: https://github.com/microsoft/durabletask-dotnet/compare/main...$branchName"
    }
    else {
        Write-Host "Pull request created successfully."
    }
}
finally {
    Pop-Location
}

Write-Host ""
Write-Host "=== Release kickoff complete ==="
