# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

BeforeAll {
    Import-Module powershell-yaml -ErrorAction Stop
    $pipeline = ConvertFrom-Yaml (Get-Content -LiteralPath "$PSScriptRoot\publish.yml" -Raw)
    $releaseTemplate = ConvertFrom-Yaml (Get-Content -LiteralPath "$PSScriptRoot\nuget-release-steps.yml" -Raw)
    $stagePackage = [scriptblock]::Create($releaseTemplate.steps[0].pwsh)
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
            $job.steps[0].template | Should -Be 'nuget-release-steps.yml'
            $job.steps[0].parameters.organizationName | Should -Be '${{ parameters.nuGetOrganization }}'
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
        $exclusions = $pipeline.parameters | Where-Object name -EQ 'skipPackages'

        # Assert
        $organization.default | Should -BeNullOrEmpty
        $exclusions.type | Should -Be 'object'
        $exclusions.default.Count | Should -Be 0
        $publicPackageIds | Should -Contain 'Microsoft.DurableTask.Generators'
    }

    It 'uses the Durable ESRP14 NuGet profile without a direct public-push fallback' {
        # Arrange / Act
        $release = $releaseTemplate.steps[1]

        # Assert
        $releaseTemplate.steps.Count | Should -Be 2
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
        $release.inputs.FolderLocation | Should -Be $releaseTemplate.steps[0].env.STAGING_FOLDER
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
        $skipPackages = @('Microsoft.DurableTask.Generators')
        $selectedJobs = @($publicJobs | Where-Object { $_.steps[0].parameters.packageId -notin $skipPackages })
        @(Get-ChildItem -LiteralPath $env:SOURCE_FOLDER -Filter '*.nupkg').Count | Should -Be 18

        # Act / Assert
        $selectedJobs.Count | Should -Be 16
        foreach ($job in $selectedJobs) {
            $env:PACKAGE_ID = $job.steps[0].parameters.packageId
            $env:STAGING_FOLDER = Join-Path $fixture $env:PACKAGE_ID
            & $stagePackage 6>$null
            $staged = @(Get-ChildItem -LiteralPath $env:STAGING_FOLDER -File)
            $staged.Count | Should -Be 1
            $original = Join-Path $env:SOURCE_FOLDER $staged[0].Name
            (Get-FileHash -LiteralPath $staged[0].FullName).Hash | Should -Be (Get-FileHash -LiteralPath $original).Hash
            $staged[0].Name | Should -Not -Match 'Generators|InProcessTestHost|symbols'
        }
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
