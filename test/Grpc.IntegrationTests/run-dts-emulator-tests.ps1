# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

param(
    [string]$Configuration = "Debug",
    [string]$Image = "mcr.microsoft.com/dts/dts-emulator:latest"
)

$ErrorActionPreference = "Stop"
$containerName = "durabletask-dotnet-dts-$([Guid]::NewGuid().ToString('N'))"
$previousConnectionString = $env:DTS_EMULATOR_CONNECTION_STRING
$containerStarted = $false
$httpClient = $null

try {
    docker info | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Docker is not available."
    }

    docker run `
        --name $containerName `
        --rm `
        --detach `
        --pull always `
        --publish "127.0.0.1::8080" `
        $Image | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to start the DTS emulator container."
    }

    $containerStarted = $true
    $publishedPort = docker port $containerName "8080/tcp"
    if ($LASTEXITCODE -ne 0 -or $publishedPort -notmatch ':(\d+)$') {
        throw "Failed to determine the DTS emulator port."
    }

    $port = [int]$Matches[1]
    $ready = $false
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    $httpClient = [System.Net.Http.HttpClient]::new()
    $httpClient.Timeout = [TimeSpan]::FromSeconds(1)
    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            $response = $httpClient.GetAsync(
                "http://127.0.0.1:$port/").GetAwaiter().GetResult()
            $response.Dispose()
            $ready = $true
            break
        }
        catch [System.Net.Http.HttpRequestException] {
        }
        catch [System.Threading.Tasks.TaskCanceledException] {
        }

        Start-Sleep -Milliseconds 250
    }

    if (-not $ready) {
        docker logs $containerName
        throw "The DTS emulator did not become ready within 30 seconds."
    }

    $env:DTS_EMULATOR_CONNECTION_STRING =
        "Endpoint=http://127.0.0.1:$port;TaskHub=default;Authentication=None"

    dotnet test `
        "$PSScriptRoot\Grpc.IntegrationTests.csproj" `
        --configuration $Configuration `
        --filter "Category=DtsEmulator"
    if ($LASTEXITCODE -ne 0) {
        docker logs $containerName
        throw "The DTS emulator integration tests failed."
    }
}
finally {
    if ($null -ne $httpClient) {
        $httpClient.Dispose()
    }

    $env:DTS_EMULATOR_CONNECTION_STRING = $previousConnectionString
    if ($containerStarted) {
        docker rm --force $containerName | Out-Null
    }
}
