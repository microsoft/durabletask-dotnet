# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

BeforeAll {
    Import-Module powershell-yaml -ErrorAction Stop
    $pipeline = ConvertFrom-Yaml (Get-Content -LiteralPath "$PSScriptRoot\publish.yml" -Raw)
    $releaseTemplate = ConvertFrom-Yaml (Get-Content -LiteralPath "$PSScriptRoot\nuget-release-steps.yml" -Raw)
    $publishSteps = @($releaseTemplate.steps[1]['${{ else }}'])
    $stagePackage = [scriptblock]::Create($publishSteps[0].pwsh)
    $jobs = $pipeline.extends.parameters.stages[0].jobs
    $publicJobs = @($jobs | Where-Object job -Like 'nugetRelease_*')
    $publicPackageIds = @(
        'Microsoft.DurableTask.Abstractions'
        'Microsoft.DurableTask.Client'
        'Microsoft.DurableTask.Client.Grpc'
        'Microsoft.DurableTask.Client.OrchestrationServiceClientShim'
        'Microsoft.DurableTask.Generators'
        'Microsoft.DurableTask.Analyzers'
        'Microsoft.DurableTask.Grpc'
        'Microsoft.DurableTask.Worker'
        'Microsoft.DurableTask.Worker.Grpc'
        'Microsoft.DurableTask.Client.AzureManaged'
        'Microsoft.DurableTask.Client.AzureManaged.Sandboxes'
        'Microsoft.DurableTask.Worker.AzureManaged'
        'Microsoft.DurableTask.Worker.AzureManaged.Sandboxes'
        'Microsoft.DurableTask.ScheduledTasks'
        'Microsoft.DurableTask.ExportHistory'
        'Microsoft.DurableTask.Extensions.AzureBlobPayloads'
        'Microsoft.DurableTask.LargePayloadPurge.Abstractions'
    )

    function New-TestPackage {
        param(
            [string]$Folder,
            [string]$PackageId,
            [string]$Version = '1.27.1',
            [string]$FileName = "$PackageId.$Version.nupkg",
            [string]$Nuspec = @"
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata><id>$PackageId</id><version>$Version</version></metadata>
</package>
"@
        )

        $path = Join-Path $Folder $FileName
        $archive = [System.IO.Compression.ZipFile]::Open($path, [System.IO.Compression.ZipArchiveMode]::Create)
        try {
            $writer = [System.IO.StreamWriter]::new($archive.CreateEntry("$PackageId.nuspec").Open())
            try {
                $writer.Write($Nuspec)
            }
            finally {
                $writer.Dispose()
            }
        }
        finally {
            $archive.Dispose()
        }
    }

    function Test-PackageListExpression {
        param([string]$Expression, [hashtable]$Parameters)

        $expressionBody = $Expression -replace '^\$\{\{\s*|\s*\}\}$', ''
        $negated = $expressionBody.StartsWith('not(')
        if ($negated) {
            $expressionBody = $expressionBody.Substring(4, $expressionBody.Length - 5)
        }
        $match = [regex]::Match($expressionBody, "^containsValue\(parameters\.(?<list>\w+), '(?<id>[^']+)'\)$")
        if (-not $match.Success -or -not $Parameters.ContainsKey($match.Groups['list'].Value)) {
            throw "Unsupported package-list expression: $Expression"
        }
        $included = @($Parameters[$match.Groups['list'].Value]) -contains $match.Groups['id'].Value
        if ($negated) {
            return -not $included
        }
        return $included
    }

    function Get-ReleaseSteps {
        param([System.Collections.IDictionary]$Job, [hashtable]$Parameters)

        $alreadyPublished = Test-PackageListExpression $Job.steps[0].parameters.alreadyPublished $Parameters
        $branch = if ($alreadyPublished) { '${{ if parameters.alreadyPublished }}' } else { '${{ else }}' }
        $branches = @($releaseTemplate.steps | Where-Object { $_.ContainsKey($branch) })
        if ($branches.Count -ne 1) {
            throw "Expected one template branch for alreadyPublished=$alreadyPublished."
        }
        return $branches[0][$branch]
    }

    # Model only the conditions emitted by this pipeline; unexpected expressions fail the local harness.
    function Test-ReleaseJobCondition {
        param([System.Collections.IDictionary]$Job, [hashtable]$Parameters, [hashtable]$Results)

        $condition = [regex]::Replace($Job.condition, '\$\{\{.*?\}\}', {
            param($match)
            (Test-PackageListExpression $match.Value $Parameters).ToString()
        })
        $match = [regex]::Match($condition, "^and\(succeeded\((?:'(?<dependency>[^']+)')?\), (?<included>True|False)\)$")
        if (-not $match.Success) {
            throw "Unsupported job condition: $condition"
        }
        if ($match.Groups['included'].Value -eq 'False') {
            return $false
        }
        $dependencies = if ($match.Groups['dependency'].Success) { @($match.Groups['dependency'].Value) } else { @($Job.dependsOn) }
        return @($dependencies | Where-Object { $Results[$_] -notin 'Succeeded', 'SucceededWithIssues' }).Count -eq 0
    }

    function Invoke-TestReleasePlan {
        param(
            [string]$Root,
            [string[]]$AlreadyPublishedPackages = @(),
            [string[]]$SkipPackages = @(),
            [hashtable]$ResultOverrides = @{},
            [string]$ApprovalResult = 'Succeeded',
            [string]$AdoResult = 'Succeeded'
        )

        $parameters = @{ alreadyPublishedPackages = $AlreadyPublishedPackages; skipPackages = $SkipPackages }
        $results = @{ AdoRelease = $AdoResult; nugetApproval = $(if ($AdoResult -eq 'Succeeded') { $ApprovalResult } else { 'Skipped' }) }
        $pending = [Collections.Generic.List[object]]::new()
        $publicJobs | ForEach-Object { $pending.Add($_) }
        $order = [Collections.Generic.List[string]]::new()
        $submissions = [Collections.Generic.List[string]]::new()
        $logs = [Collections.Generic.List[string]]::new()
        $errors = @{}
        $savedEnvironment = @{}
        foreach ($name in 'PACKAGE_ID', 'NUGET_ORGANIZATION', 'SOURCE_FOLDER', 'STAGING_FOLDER') {
            $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
        }
        try {
            while ($pending.Count) {
                $ready = @($pending | Where-Object { @($_.dependsOn | Where-Object { -not $results.ContainsKey($_) }).Count -eq 0 })
                if ($ready.Count -eq 0) {
                    throw 'The public job graph cannot make progress.'
                }
                foreach ($job in $ready) {
                    $packageId = $job.steps[0].parameters.packageId
                    if (-not (Test-ReleaseJobCondition $job $parameters $results)) {
                        $results[$job.job] = 'Skipped'
                    }
                    elseif ($ResultOverrides.ContainsKey($packageId)) {
                        $results[$job.job] = $ResultOverrides[$packageId]
                    }
                    else {
                        $order.Add($packageId)
                        try {
                            foreach ($step in Get-ReleaseSteps $job $parameters) {
                                if ($step.pwsh) {
                                    foreach ($entry in $step.env.GetEnumerator()) {
                                        $value = $entry.Value.Replace('${{ parameters.packageId }}', $packageId).
                                            Replace('${{ parameters.organizationName }}', 'validation-only-organization').
                                            Replace('$(System.DefaultWorkingDirectory)', $Root)
                                        [Environment]::SetEnvironmentVariable($entry.Key, $value)
                                    }
                                    & ([scriptblock]::Create($step.pwsh)) 6>&1 | ForEach-Object { $logs.Add([string]$_) }
                                }
                                elseif ($step.task -eq 'SFP.release-tasks.custom-build-release-task.EsrpRelease@14') {
                                    # Record the emitted task, never invoke a distribution tool in local tests.
                                    $submissions.Add($packageId)
                                }
                                else {
                                    throw 'Unsupported release step in local harness.'
                                }
                            }
                            $results[$job.job] = 'Succeeded'
                        }
                        catch {
                            $results[$job.job] = 'Failed'
                            $errors[$packageId] = $_.Exception.Message
                        }
                    }
                    [void]$pending.Remove($job)
                }
            }
        }
        finally {
            foreach ($name in $savedEnvironment.Keys) {
                [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name])
            }
        }
        $packageResults = @{}
        foreach ($job in $publicJobs) {
            $packageResults[$job.steps[0].parameters.packageId] = $results[$job.job]
        }
        return [pscustomobject]@{
            Results = $packageResults
            Order = $order.ToArray()
            Submissions = $submissions.ToArray()
            Logs = $logs.ToArray()
            Errors = $errors
        }
    }
}

Describe 'Publisher pipeline' {
    It 'retains exactly the existing 17 public package jobs and signed artifact inputs' {
        # Arrange / Act
        $actualIds = @($publicJobs | ForEach-Object { $_.steps[0].parameters.packageId })

        # Assert
        $publicJobs.Count | Should -Be 17
        ($actualIds | Sort-Object) -join ',' | Should -Be (($publicPackageIds | Sort-Object) -join ',')
        foreach ($job in $publicJobs) {
            $packageId = $job.steps[0].parameters.packageId
            $job.job | Should -Be ("nugetRelease_" + $packageId.Replace('.', '_'))
            $job.templateContext.type | Should -Be 'releaseJob'
            $job.templateContext.isProduction | Should -BeTrue
            $job.templateContext.inputs[0].pipeline | Should -Be 'officialPipeline'
            $job.templateContext.inputs[0].artifactName | Should -Be 'drop'
            $job.steps[0].parameters.organizationName | Should -Be '${{ parameters.nuGetOrganization }}'
            Test-PackageListExpression $job.steps[0].parameters.alreadyPublished @{ alreadyPublishedPackages = @($packageId) } | Should -BeTrue
            Test-PackageListExpression $job.steps[0].parameters.alreadyPublished @{ alreadyPublishedPackages = @() } | Should -BeFalse
        }
    }

    It 'anchors every NuGet step template to self when passed through the external 1ES stage template' {
        # Arrange
        $expectedTemplate = '/eng/publish/nuget-release-steps.yml@self'

        # Act
        $references = @($publicJobs | ForEach-Object { $_.steps[0].template })

        # Assert
        $references.Count | Should -Be 17
        foreach ($reference in $references) {
            $reference | Should -Be $expectedTemplate
        }
    }

    It 'preserves internal staging and waits for it before manual NuGet approval' {
        # Arrange
        $ado = $jobs | Where-Object job -EQ 'AdoRelease'
        $approval = $jobs | Where-Object job -EQ 'nugetApproval'

        # Act / Assert
        $pipeline.trigger | Should -Be 'none'
        $pipeline.pr | Should -Be 'none'
        $pipeline.resources.pipelines[0].source | Should -Be 'durabletask-dotnet.official'
        $ado.steps[0].task | Should -Be '1ES.PublishNuget@1'
        $ado.steps[0].inputs.publishVstsFeed | Should -Be '3f99e810-c336-441f-8892-84983093ad7f/c895696b-ce37-4fe7-b7ce-74333a04f8bf'
        $ado.steps[0].inputs.allowPackageConflicts | Should -BeTrue
        $ado.steps[0].inputs.packagesToPush | Should -Be '$(System.DefaultWorkingDirectory)/**/*.nupkg;!$(System.DefaultWorkingDirectory)/**/*.symbols.nupkg'
        $approval.dependsOn | Should -Be 'AdoRelease'
        $approval.pool | Should -Be 'server'
        $approval.steps[0].task | Should -Be 'ManualValidation@1'
        $approval.steps[0].inputs.onTimeout | Should -Be 'reject'
        $approval.steps[0].inputs.instructions | Should -Match ([regex]::Escape('${{ parameters.nuGetOrganization }}'))
        $approval.steps[0].inputs.instructions | Should -Match 'ESRP NuGet onboarding'
        $approval.steps[0].inputs.instructions | Should -Match 'ESRPRELPACMAN'
        $approval.steps[0].inputs.instructions | Should -Match 'Trusted Publishing'
        $approval.steps[0].inputs.instructions | Should -Match 'Stop here for a validation-only trial'
        $approval.steps[0].inputs.instructions | Should -Match ([regex]::Escape("join(', ', parameters.skipPackages)"))
        $approval.steps[0].inputs.instructions | Should -Match ([regex]::Escape("join(', ', parameters.alreadyPublishedPackages)"))
        $approval.steps[0].inputs.instructions | Should -Match 'exact selected artifact version'
        $approval.steps[0].inputs.instructions | Should -Match 'Intentional holds take precedence'
    }

    It 'retains fail-closed dependency gates and run-scoped exclusions for every public job' {
        # Arrange / Act / Assert
        foreach ($job in $publicJobs) {
            $packageId = $job.steps[0].parameters.packageId
            @($job.dependsOn) | Should -Contain 'nugetApproval'
            $job.condition | Should -Match ([regex]::Escape("not(containsValue(parameters.skipPackages, '$packageId'))"))
            $job.condition | Should -Match '^and\(succeeded\('
        }

        $contract = $jobs | Where-Object job -EQ 'nugetRelease_Microsoft_DurableTask_LargePayloadPurge_Abstractions'
        ($contract.dependsOn -join ',') | Should -Be 'nugetApproval,nugetRelease_Microsoft_DurableTask_Abstractions,nugetRelease_Microsoft_DurableTask_Client'
        $contract.condition | Should -Match '^and\(succeeded\(\),'
        $blob = $jobs | Where-Object job -EQ 'nugetRelease_Microsoft_DurableTask_Extensions_AzureBlobPayloads'
        ($blob.dependsOn -join ',') | Should -Be 'nugetApproval,nugetRelease_Microsoft_DurableTask_LargePayloadPurge_Abstractions'
        $blob.condition | Should -Match '^and\(succeeded\(\),'
    }

    It 'requires an explicit organization and leaves future generator releases enabled by default' {
        # Arrange / Act
        $organization = $pipeline.parameters | Where-Object name -EQ 'nuGetOrganization'
        $holds = $pipeline.parameters | Where-Object name -EQ 'skipPackages'
        $published = $pipeline.parameters | Where-Object name -EQ 'alreadyPublishedPackages'
        $noOp = $releaseTemplate.parameters | Where-Object name -EQ 'alreadyPublished'

        # Assert
        $organization.default | Should -BeNullOrEmpty
        $holds.type | Should -Be 'object'
        $holds.default.Count | Should -Be 0
        $published.type | Should -Be 'object'
        $published.default.Count | Should -Be 0
        $noOp.type | Should -Be 'boolean'
        $noOp.default | Should -BeFalse
        $publicPackageIds | Should -Contain 'Microsoft.DurableTask.Generators'
    }

    It 'uses the Durable ESRP14 NuGet profile without a direct public-push fallback' {
        # Arrange / Act
        $release = $publishSteps[1]

        # Assert
        $publishSteps.Count | Should -Be 2
        $release.task | Should -Be 'SFP.release-tasks.custom-build-release-task.EsrpRelease@14'
        $release.inputs.ConnectedServiceName | Should -Be 'dtfx-internal-esrp-prod'
        $release.inputs.ClientId | Should -Be '0b3ed1a4-0727-4a50-b82a-02c2bd9dec89'
        $release.inputs.DomainTenantId | Should -Be '33e01921-4d64-4f8c-a055-5bdaffd5e33d'
        $release.inputs.UseManagedIdentity | Should -BeTrue
        $release.inputs.KeyVaultName | Should -Be 'durable-esrp-akv'
        $release.inputs.SignCertName | Should -Be 'dts-esrp-cert'
        $release.inputs.Intent | Should -Be 'PackageDistribution'
        $release.inputs.ContentType | Should -Be 'NuGet'
        $release.inputs.OrganizationName | Should -Be '${{ parameters.organizationName }}'
        $release.inputs.ContentSource | Should -Be 'Folder'
        $release.inputs.FolderLocation | Should -Be $publishSteps[0].env.STAGING_FOLDER
        $release.inputs.WaitForReleaseCompletion | Should -BeTrue
        $release.inputs.MainPublisher | Should -Be 'ESRPRELPACMAN'
        $release.inputs.Owners | Should -Be '$(Build.RequestedForEmail)'
        $release.inputs.Approvers | Should -Be '$(Build.RequestedForEmail)'
        @($jobs.steps | Where-Object task -EQ '1ES.PublishNuget@1').Count | Should -Be 1
    }
}

Describe 'ESRP package staging' {
    BeforeAll {
        $environmentNames = 'PACKAGE_ID', 'NUGET_ORGANIZATION', 'SOURCE_FOLDER', 'STAGING_FOLDER'
        $savedEnvironment = @{}
        foreach ($name in $environmentNames) {
            $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
        }
    }

    BeforeEach {
        $fixture = Join-Path $TestDrive ([guid]::NewGuid().ToString())
        $env:SOURCE_FOLDER = Join-Path $fixture 'drop'
        $env:STAGING_FOLDER = Join-Path $fixture 'esrp'
        $env:PACKAGE_ID = 'Microsoft.DurableTask.Client'
        $env:NUGET_ORGANIZATION = 'validation-only-organization'
        New-Item -ItemType Directory -Path $env:SOURCE_FOLDER | Out-Null
        foreach ($id in $publicPackageIds) {
            $version = switch ($id) {
                'Microsoft.DurableTask.Generators' { '2.1.0-preview.2' }
                'Microsoft.DurableTask.ScheduledTasks' { '1.27.1-preview.2' }
                'Microsoft.DurableTask.ExportHistory' { '1.27.1-preview.2' }
                default { '1.27.1' }
            }
            New-TestPackage -Folder $env:SOURCE_FOLDER -PackageId $id -Version $version
        }
        New-TestPackage -Folder $env:SOURCE_FOLDER -PackageId 'Microsoft.DurableTask.InProcessTestHost' -Version '0.2.3-preview.1'
    }

    AfterAll {
        foreach ($name in $environmentNames) {
            [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name])
        }
    }

    It 'stages exactly 16 unchanged trial packages from the 18-package drop, excluding the old generator and test host' {
        # Arrange
        @(Get-ChildItem -LiteralPath $env:SOURCE_FOLDER -Filter '*.nupkg').Count | Should -Be 18

        # Act
        $plan = Invoke-TestReleasePlan -Root $fixture -AlreadyPublishedPackages 'Microsoft.DurableTask.Generators'

        # Assert
        $plan.Errors.Count | Should -Be 0
        $plan.Submissions.Count | Should -Be 16
        $plan.Results['Microsoft.DurableTask.Generators'] | Should -Be 'Succeeded'
        $plan.Logs -join "`n" | Should -Match "Operator confirmed.*Microsoft.DurableTask.Generators.*No package will be staged or submitted"
        Test-Path -LiteralPath "$fixture\esrp\Microsoft.DurableTask.Generators" | Should -BeFalse
        foreach ($packageId in $plan.Submissions) {
            $staged = @(Get-ChildItem -LiteralPath "$fixture\esrp\$packageId" -File)
            $staged.Count | Should -Be 1
            $original = Join-Path $env:SOURCE_FOLDER $staged[0].Name
            (Get-FileHash -LiteralPath $staged[0].FullName).Hash | Should -Be (Get-FileHash -LiteralPath $original).Hash
            $staged[0].Name | Should -Not -Match 'Generators|InProcessTestHost|symbols'
        }
    }

    It 'emits and executes only a logged no-op for already-public versions, even without a source folder' {
        # Arrange
        $noArtifacts = Join-Path $fixture 'no-artifacts'

        # Act
        $plan = Invoke-TestReleasePlan -Root $noArtifacts -AlreadyPublishedPackages $publicPackageIds

        # Assert
        $plan.Errors.Count | Should -Be 0
        @($plan.Results.Values | Where-Object { $_ -eq 'Succeeded' }).Count | Should -Be 17
        $plan.Submissions.Count | Should -Be 0
        @($plan.Logs | Where-Object { $_ -match 'Operator confirmed.*No package will be staged or submitted' }).Count | Should -Be 17
        Test-Path -LiteralPath $noArtifacts | Should -BeFalse
        foreach ($job in $publicJobs) {
            $steps = @(Get-ReleaseSteps $job @{ alreadyPublishedPackages = $publicPackageIds })
            $steps.Count | Should -Be 1
            @($steps | Where-Object task).Count | Should -Be 0
        }
    }

    It 'runs the contract then Blob after successful no-ops for already-public Client and Abstractions' {
        # Arrange
        $prerequisites = 'Microsoft.DurableTask.Client', 'Microsoft.DurableTask.Abstractions'
        $contract = 'Microsoft.DurableTask.LargePayloadPurge.Abstractions'
        $blob = 'Microsoft.DurableTask.Extensions.AzureBlobPayloads'

        # Act
        $plan = Invoke-TestReleasePlan -Root $fixture -AlreadyPublishedPackages $prerequisites

        # Assert
        $plan.Errors.Count | Should -Be 0
        foreach ($id in $prerequisites) {
            $plan.Results[$id] | Should -Be 'Succeeded'
            $plan.Submissions | Should -Not -Contain $id
            Test-Path -LiteralPath "$fixture\esrp\$id" | Should -BeFalse
            [array]::IndexOf($plan.Order, $id) | Should -BeLessThan ([array]::IndexOf($plan.Order, $contract))
        }
        $plan.Results[$contract] | Should -Be 'Succeeded'
        $plan.Results[$blob] | Should -Be 'Succeeded'
        $plan.Submissions | Should -Contain $contract
        $plan.Submissions | Should -Contain $blob
        [array]::IndexOf($plan.Order, $contract) | Should -BeLessThan ([array]::IndexOf($plan.Order, $blob))
    }

    It 'can retry only Blob when the contract and other selected versions are already public' {
        # Arrange
        $blob = 'Microsoft.DurableTask.Extensions.AzureBlobPayloads'
        $published = @($publicPackageIds | Where-Object { $_ -ne $blob })

        # Act
        $plan = Invoke-TestReleasePlan -Root $fixture -AlreadyPublishedPackages $published

        # Assert
        $plan.Errors.Count | Should -Be 0
        $plan.Results['Microsoft.DurableTask.LargePayloadPurge.Abstractions'] | Should -Be 'Succeeded'
        $plan.Results[$blob] | Should -Be 'Succeeded'
        $plan.Submissions.Count | Should -Be 1
        $plan.Submissions[0] | Should -Be $blob
        @(Get-ChildItem -LiteralPath "$fixture\esrp" -Directory).Count | Should -Be 1
        Test-Path -LiteralPath "$fixture\esrp\Microsoft.DurableTask.LargePayloadPurge.Abstractions" | Should -BeFalse
    }

    It 'gives a true hold precedence over an already-public confirmation for <Prerequisite>' -TestCases @(
        @{ Prerequisite = 'Microsoft.DurableTask.Client' }
        @{ Prerequisite = 'Microsoft.DurableTask.Abstractions' }
        @{ Prerequisite = 'Microsoft.DurableTask.LargePayloadPurge.Abstractions' }
    ) {
        param($Prerequisite)
        # Arrange / Act
        $plan = Invoke-TestReleasePlan -Root $fixture -AlreadyPublishedPackages $Prerequisite -SkipPackages $Prerequisite

        # Assert
        $plan.Errors.Count | Should -Be 0
        $plan.Results[$Prerequisite] | Should -Be 'Skipped'
        $plan.Results['Microsoft.DurableTask.LargePayloadPurge.Abstractions'] | Should -Be 'Skipped'
        $plan.Results['Microsoft.DurableTask.Extensions.AzureBlobPayloads'] | Should -Be 'Skipped'
        $plan.Submissions | Should -Not -Contain $Prerequisite
        $plan.Logs -join "`n" | Should -Not -Match ([regex]::Escape("Operator confirmed the selected version of '$Prerequisite'"))
    }

    It 'blocks dependents after a real staging failure in <Prerequisite>' -TestCases @(
        @{ Prerequisite = 'Microsoft.DurableTask.Client' }
        @{ Prerequisite = 'Microsoft.DurableTask.Abstractions' }
        @{ Prerequisite = 'Microsoft.DurableTask.LargePayloadPurge.Abstractions' }
    ) {
        param($Prerequisite)
        # Arrange
        Remove-Item -LiteralPath "$env:SOURCE_FOLDER\$Prerequisite.1.27.1.nupkg"

        # Act
        $plan = Invoke-TestReleasePlan -Root $fixture

        # Assert
        $plan.Results[$Prerequisite] | Should -Be 'Failed'
        $plan.Errors[$Prerequisite] | Should -BeLike '*found 0*'
        $plan.Results['Microsoft.DurableTask.Extensions.AzureBlobPayloads'] | Should -Be 'Skipped'
        $plan.Submissions | Should -Not -Contain $Prerequisite
        if ($Prerequisite -ne 'Microsoft.DurableTask.LargePayloadPurge.Abstractions') {
            $plan.Results['Microsoft.DurableTask.LargePayloadPurge.Abstractions'] | Should -Be 'Skipped'
        }
    }

    It 'blocks dependents when the confirmed prerequisite job is canceled: <Prerequisite>' -TestCases @(
        @{ Prerequisite = 'Microsoft.DurableTask.Client' }
        @{ Prerequisite = 'Microsoft.DurableTask.Abstractions' }
        @{ Prerequisite = 'Microsoft.DurableTask.LargePayloadPurge.Abstractions' }
    ) {
        param($Prerequisite)
        # Arrange
        $overrides = @{}
        $overrides[$Prerequisite] = 'Canceled'

        # Act
        $plan = Invoke-TestReleasePlan -Root $fixture -AlreadyPublishedPackages $Prerequisite -ResultOverrides $overrides

        # Assert
        $plan.Errors.Count | Should -Be 0
        $plan.Results[$Prerequisite] | Should -Be 'Canceled'
        $plan.Results['Microsoft.DurableTask.Extensions.AzureBlobPayloads'] | Should -Be 'Skipped'
        if ($Prerequisite -ne 'Microsoft.DurableTask.LargePayloadPurge.Abstractions') {
            $plan.Results['Microsoft.DurableTask.LargePayloadPurge.Abstractions'] | Should -Be 'Skipped'
        }
    }

    It 'does not run even already-public no-ops when approval is <ApprovalResult>' -TestCases @(
        @{ ApprovalResult = 'Failed' }
        @{ ApprovalResult = 'Canceled' }
        @{ ApprovalResult = 'Skipped' }
    ) {
        param($ApprovalResult)
        # Arrange / Act
        $plan = Invoke-TestReleasePlan -Root $fixture -AlreadyPublishedPackages $publicPackageIds -ApprovalResult $ApprovalResult

        # Assert
        @($plan.Results.Values | Where-Object { $_ -eq 'Skipped' }).Count | Should -Be 17
        $plan.Submissions.Count | Should -Be 0
        $plan.Logs.Count | Should -Be 0
        Test-Path -LiteralPath "$fixture\esrp" | Should -BeFalse
    }

    It 'blocks all public jobs when internal staging fails' {
        # Arrange / Act
        $plan = Invoke-TestReleasePlan -Root $fixture -AlreadyPublishedPackages $publicPackageIds -AdoResult 'Failed'

        # Assert
        @($plan.Results.Values | Where-Object { $_ -eq 'Skipped' }).Count | Should -Be 17
        $plan.Submissions.Count | Should -Be 0
        $plan.Logs.Count | Should -Be 0
    }

    It 'selects the exact nuspec ID rather than a filename prefix or a misleading filename' {
        # Arrange
        New-TestPackage -Folder $env:SOURCE_FOLDER -PackageId 'Microsoft.DurableTask.Client.2.Tools'
        New-TestPackage -Folder $env:SOURCE_FOLDER -PackageId 'Microsoft.DurableTask.InProcessTestHost' -FileName 'Microsoft.DurableTask.Client.100.0.0.nupkg'

        # Act
        & $stagePackage 6>$null

        # Assert
        $staged = @(Get-ChildItem -LiteralPath $env:STAGING_FOLDER -File)
        $staged.Count | Should -Be 1
        $staged[0].Name | Should -Be 'Microsoft.DurableTask.Client.1.27.1.nupkg'
    }

    It 'excludes legacy symbols and snupkg files even when their package ID matches' {
        # Arrange
        Set-Content -LiteralPath "$env:SOURCE_FOLDER\Microsoft.DurableTask.Client.1.27.1.symbols.nupkg" -Value 'not a product package'
        Set-Content -LiteralPath "$env:SOURCE_FOLDER\Microsoft.DurableTask.Client.1.27.1.snupkg" -Value 'not a product package'

        # Act
        & $stagePackage 6>$null

        # Assert
        @(Get-ChildItem -LiteralPath $env:STAGING_FOLDER -File).Count | Should -Be 1
        Test-Path -LiteralPath "$env:STAGING_FOLDER\Microsoft.DurableTask.Client.1.27.1.nupkg" | Should -BeTrue
    }

    It 'can stage a future generator version without a permanent generator exclusion' {
        # Arrange
        $env:PACKAGE_ID = 'Microsoft.DurableTask.Generators'
        Remove-Item -LiteralPath "$env:SOURCE_FOLDER\Microsoft.DurableTask.Generators.2.1.0-preview.2.nupkg"
        New-TestPackage -Folder $env:SOURCE_FOLDER -PackageId $env:PACKAGE_ID -Version '2.1.0-preview.3'

        # Act
        & $stagePackage 6>$null

        # Assert
        $staged = @(Get-ChildItem -LiteralPath $env:STAGING_FOLDER -File)
        $staged.Count | Should -Be 1
        $staged[0].Name | Should -Be 'Microsoft.DurableTask.Generators.2.1.0-preview.3.nupkg'
    }

    It 'rejects an unset or unresolved organization: <Organization>' -TestCases @(
        @{ Organization = '' }
        @{ Organization = ' ' }
        @{ Organization = '$(NuGetOrganizationName)' }
        @{ Organization = '${{ parameters.organizationName }}' }
    ) {
        param($Organization)
        # Arrange
        $env:NUGET_ORGANIZATION = $Organization

        # Act / Assert
        { & $stagePackage 6>$null } | Should -Throw '*Set nuGetOrganization*'
        Test-Path -LiteralPath $env:STAGING_FOLDER | Should -BeFalse
    }

    It 'rejects a missing package without creating a submission folder' {
        # Arrange
        $env:PACKAGE_ID = 'Microsoft.DurableTask.Missing'

        # Act / Assert
        { & $stagePackage 6>$null } | Should -Throw '*found 0*'
        Test-Path -LiteralPath $env:STAGING_FOLDER | Should -BeFalse
    }

    It 'rejects multiple versions of a package rather than choosing one silently' {
        # Arrange
        New-TestPackage -Folder $env:SOURCE_FOLDER -PackageId $env:PACKAGE_ID -Version '1.27.0'

        # Act / Assert
        { & $stagePackage 6>$null } | Should -Throw '*found 2*'
        Test-Path -LiteralPath $env:STAGING_FOLDER | Should -BeFalse
    }

    It 'rejects malformed package metadata' {
        # Arrange
        New-TestPackage -Folder $env:SOURCE_FOLDER -PackageId 'Invalid' -Nuspec '<package><metadata /></package>'

        # Act / Assert
        { & $stagePackage 6>$null } | Should -Throw "*Missing package ID in 'Invalid.1.27.1.nupkg'*"
        Test-Path -LiteralPath $env:STAGING_FOLDER | Should -BeFalse
    }

    It 'refuses to reuse or delete a nonempty staging folder' {
        # Arrange
        New-Item -ItemType Directory -Path $env:STAGING_FOLDER | Out-Null
        $stalePackage = Join-Path $env:STAGING_FOLDER 'stale.nupkg'
        Set-Content -LiteralPath $stalePackage -Value 'stale content'

        # Act / Assert
        { & $stagePackage 6>$null } | Should -Throw '*refusing to reuse*'
        Get-Content -LiteralPath $stalePackage | Should -Be 'stale content'
        @(Get-ChildItem -LiteralPath $env:STAGING_FOLDER -File).Count | Should -Be 1
    }
}
