# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

BeforeAll {
    Import-Module powershell-yaml -ErrorAction Stop
    $pipeline = ConvertFrom-Yaml (Get-Content "$PSScriptRoot\publish.yml" -Raw)
    $template = ConvertFrom-Yaml (Get-Content "$PSScriptRoot\nuget-release-steps.yml" -Raw)
    $publishSteps = @($template.steps[1]['${{ else }}'])
    $noOpSteps = @($template.steps[0]['${{ if parameters.alreadyPublished }}'])
    $stagePackage = [scriptblock]::Create($publishSteps[0].pwsh)
    $confirmPublished = [scriptblock]::Create($noOpSteps[0].pwsh)
    $jobs = $pipeline.extends.parameters.stages[0].jobs
    $publicJobs = @($jobs | Where-Object job -Like 'nugetRelease_*')

    function New-TestPackage {
        param(
            [string]$Id,
            [string]$Version = '1.0.0',
            [string]$FileName = "$Id.$Version.nupkg",
            [string]$Nuspec = "<package xmlns='http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd'><metadata><id>$Id</id><version>$Version</version></metadata></package>"
        )
        $archive = [IO.Compression.ZipFile]::Open((Join-Path $env:SOURCE_FOLDER $FileName), 'Create')
        try {
            $writer = [IO.StreamWriter]::new($archive.CreateEntry("$Id.nuspec").Open())
            try { $writer.Write($Nuspec) }
            finally { $writer.Dispose() }
        }
        finally { $archive.Dispose() }
    }
}

Describe 'Publisher wiring' {
    It 'retains 17 unique public identities with explicit self templates and per-package confirmations' {
        $jobs.Count | Should -Be 19
        $publicJobs.Count | Should -Be 17
        $ids = @($publicJobs | ForEach-Object { $_.steps[0].parameters.packageId })
        @($ids | Select-Object -Unique).Count | Should -Be 17
        $ids | Should -Not -Contain 'Microsoft.DurableTask.InProcessTestHost'
        foreach ($job in $publicJobs) {
            $id = $job.steps[0].parameters.packageId
            $job.job | Should -Be ("nugetRelease_" + $id.Replace('.', '_'))
            $job.steps.Count | Should -Be 1
            $job.steps[0].template | Should -Be '/eng/publish/nuget-release-steps.yml@self'
            $job.steps[0].parameters.organizationName | Should -Be '${{ parameters.nuGetOrganization }}'
            $job.steps[0].parameters.alreadyPublished | Should -Be ("`${{ containsValue(parameters.alreadyPublishedPackages, '$id') }}")
            $job.templateContext.inputs[0].pipeline | Should -Be 'officialPipeline'
            $job.templateContext.inputs[0].artifactName | Should -Be 'drop'
        }
        ($pipeline.parameters | Where-Object name -EQ 'nuGetOrganization').default | Should -BeNullOrEmpty
        ($pipeline.parameters | Where-Object name -EQ 'alreadyPublishedPackages').default.Count | Should -Be 0
        ($template.parameters | Where-Object name -EQ 'alreadyPublished').default | Should -BeFalse
    }

    It 'preserves internal staging, approval and dependency success gates' {
        $pipeline.trigger | Should -Be 'none'
        $pipeline.pr | Should -Be 'none'
        $ado = $jobs | Where-Object job -EQ 'AdoRelease'
        $ado.steps[0].task | Should -Be '1ES.PublishNuget@1'
        $ado.steps[0].inputs.allowPackageConflicts | Should -BeTrue
        $approval = $jobs | Where-Object job -EQ 'nugetApproval'
        $approval.dependsOn | Should -Be 'AdoRelease'
        $approval.pool | Should -Be 'server'
        $approval.steps[0].task | Should -Be 'ManualValidation@1'
        $approval.steps[0].inputs.onTimeout | Should -Be 'reject'
        $approval.steps[0].inputs.instructions | Should -Match 'validation-only'
        $approval.steps[0].inputs.instructions | Should -Match 'real public ESRP'
        foreach ($job in $publicJobs) {
            @($job.dependsOn) | Should -Contain 'nugetApproval'
            $expected = if (@($job.dependsOn).Count -eq 1) { "succeeded('nugetApproval')" } else { 'succeeded()' }
            $job.condition | Should -Be $expected
        }
        $contract = $jobs | Where-Object job -EQ 'nugetRelease_Microsoft_DurableTask_LargePayloadPurge_Abstractions'
        $contract.dependsOn -join ',' | Should -Be 'nugetApproval,nugetRelease_Microsoft_DurableTask_Abstractions,nugetRelease_Microsoft_DurableTask_Client'
        $blob = $jobs | Where-Object job -EQ 'nugetRelease_Microsoft_DurableTask_Extensions_AzureBlobPayloads'
        $blob.dependsOn -join ',' | Should -Be 'nugetApproval,nugetRelease_Microsoft_DurableTask_LargePayloadPurge_Abstractions'
    }

    It 'keeps distribution only in the normal branch with the Durable ESRP14 profile' {
        $noOpSteps.Count | Should -Be 1
        @($noOpSteps | Where-Object task).Count | Should -Be 0
        $publishSteps.Count | Should -Be 2
        $publishSteps[1].task | Should -Be 'SFP.release-tasks.custom-build-release-task.EsrpRelease@14'
        $expected = @{
            ConnectedServiceName = 'dtfx-internal-esrp-prod'
            ClientId = '0b3ed1a4-0727-4a50-b82a-02c2bd9dec89'
            DomainTenantId = '33e01921-4d64-4f8c-a055-5bdaffd5e33d'
            UseManagedIdentity = $true
            KeyVaultName = 'durable-esrp-akv'
            SignCertName = 'dts-esrp-cert'
            Intent = 'PackageDistribution'
            ContentType = 'NuGet'
            ContentSource = 'Folder'
            OrganizationName = '${{ parameters.organizationName }}'
            FolderLocation = $publishSteps[0].env.STAGING_FOLDER
            MainPublisher = 'ESRPRELPACMAN'
            WaitForReleaseCompletion = $true
            Owners = '$(Build.RequestedForEmail)'
            Approvers = '$(Build.RequestedForEmail)'
        }
        foreach ($key in $expected.Keys) {
            $publishSteps[1].inputs[$key] | Should -Be $expected[$key]
        }
    }
}

Describe 'Actual package scripts' {
    BeforeAll {
        $savedEnvironment = @{}
        foreach ($name in 'PACKAGE_ID', 'NUGET_ORGANIZATION', 'SOURCE_FOLDER', 'STAGING_FOLDER') {
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
        New-TestPackage $env:PACKAGE_ID
    }
    AfterAll {
        foreach ($name in $savedEnvironment.Keys) {
            [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name])
        }
    }

    It 'copies only the exact nuspec ID unchanged, excluding prefixes, disguised internal packages and symbols' {
        $original = Join-Path $env:SOURCE_FOLDER "$env:PACKAGE_ID.1.0.0.nupkg"
        $hash = (Get-FileHash $original).Hash
        New-TestPackage 'Microsoft.DurableTask.Client.2.Tools'
        New-TestPackage 'Microsoft.DurableTask.InProcessTestHost' -FileName 'Microsoft.DurableTask.Client.100.0.0.nupkg'
        New-TestPackage $env:PACKAGE_ID -FileName "$env:PACKAGE_ID.1.0.0.symbols.nupkg"
        Set-Content "$env:SOURCE_FOLDER\$env:PACKAGE_ID.1.0.0.snupkg" 'not a product package'

        & $stagePackage 6>$null

        $staged = @(Get-ChildItem $env:STAGING_FOLDER -File)
        $staged.Count | Should -Be 1
        $staged[0].Name | Should -Be "$env:PACKAGE_ID.1.0.0.nupkg"
        (Get-FileHash $staged[0].FullName).Hash | Should -Be $hash
        (Get-FileHash $original).Hash | Should -Be $hash
    }

    It 'logs an already-public <Id> without reading or staging packages' -TestCases @(
        @{ Id = 'Microsoft.DurableTask.Client' }
        @{ Id = 'Microsoft.DurableTask.Abstractions' }
        @{ Id = 'Microsoft.DurableTask.LargePayloadPurge.Abstractions' }
        @{ Id = 'Microsoft.DurableTask.Generators' }
    ) {
        param($Id)
        $env:PACKAGE_ID = $Id
        $env:SOURCE_FOLDER = Join-Path $fixture 'not-created'
        $output = @(& $confirmPublished 6>&1) -join "`n"
        $output | Should -Match ([regex]::Escape("Operator confirmed the selected version of '$Id'"))
        $output | Should -Match 'No package will be staged or submitted to ESRP'
        Test-Path $env:SOURCE_FOLDER | Should -BeFalse
        Test-Path $env:STAGING_FOLDER | Should -BeFalse
    }

    It 'stages a new Generator version through the normal branch' {
        $env:PACKAGE_ID = 'Microsoft.DurableTask.Generators'
        New-TestPackage $env:PACKAGE_ID -Version '2.0.0-preview.1'
        & $stagePackage 6>$null
        @(Get-ChildItem $env:STAGING_FOLDER -File).Name | Should -Be 'Microsoft.DurableTask.Generators.2.0.0-preview.1.nupkg'
    }

    It 'rejects unset or unresolved organization <Organization>' -TestCases @(
        @{ Organization = '' }
        @{ Organization = ' ' }
        @{ Organization = '$(NuGetOrganizationName)' }
        @{ Organization = '${{ parameters.organizationName }}' }
    ) {
        param($Organization)
        $env:NUGET_ORGANIZATION = $Organization
        { & $stagePackage 6>$null } | Should -Throw '*Set nuGetOrganization*'
        Test-Path $env:STAGING_FOLDER | Should -BeFalse
    }

    It 'rejects <Problem> packages' -TestCases @(
        @{ Problem = 'missing'; Count = 0 }
        @{ Problem = 'ambiguous'; Count = 2 }
    ) {
        param($Problem, $Count)
        if ($Problem -eq 'missing') { $env:PACKAGE_ID = 'Microsoft.DurableTask.Missing' }
        else { New-TestPackage $env:PACKAGE_ID -Version '1.0.1' }
        { & $stagePackage 6>$null } | Should -Throw "*found $Count*"
        Test-Path $env:STAGING_FOLDER | Should -BeFalse
    }

    It 'rejects missing package metadata' {
        New-TestPackage 'Invalid' -Nuspec '<package><metadata /></package>'
        { & $stagePackage 6>$null } | Should -Throw '*Missing package ID*'
        Test-Path $env:STAGING_FOLDER | Should -BeFalse
    }

    It 'refuses a stale staging folder without deleting its contents' {
        New-Item -ItemType Directory -Path $env:STAGING_FOLDER | Out-Null
        $stale = Join-Path $env:STAGING_FOLDER 'stale.nupkg'
        Set-Content $stale 'stale content'
        { & $stagePackage 6>$null } | Should -Throw '*refusing to reuse*'
        Get-Content $stale | Should -Be 'stale content'
        @(Get-ChildItem $env:STAGING_FOLDER -File).Count | Should -Be 1
    }
}
