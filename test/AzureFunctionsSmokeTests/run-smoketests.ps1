#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Runs smoke tests for the Azure Functions app using Docker containers.
.DESCRIPTION
    This script builds and publishes the Azure Functions smoke test app,
    starts required containers (Azurite for storage emulation and the Functions app),
    triggers the orchestration, and validates successful completion.
.PARAMETER ImageName
    Docker image name for the Functions app (default: "azurefunctions-smoketests")
.PARAMETER ContainerName
    Docker container name for the Functions app (default: "azurefunctions-smoketests-container")
.PARAMETER Port
    Port to expose the Functions app on (default: 8080)
.PARAMETER Timeout
    Timeout in seconds to wait for orchestration completion (default: 120)
#>

param(
    [string]$ImageName = "azurefunctions-smoketests",
    [string]$ContainerName = "azurefunctions-smoketests-container",
    [int]$Port = 8080,
    [int]$Timeout = 120
)

$ErrorActionPreference = "Stop"

# Get the directory where the script is located
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = $scriptDir
$publishDir = Join-Path $projectDir "publish"

Write-Host "=== Azure Functions Smoke Test Runner ===" -ForegroundColor Cyan
Write-Host ""

# Function to clean up containers
function Cleanup {
    Write-Host "Cleaning up containers..." -ForegroundColor Yellow
    
    # Stop and remove the Functions app container
    docker stop $ContainerName 2>$null
    docker rm $ContainerName 2>$null
    
    # Stop and remove Azurite container
    docker stop azurite-smoketest 2>$null
    docker rm azurite-smoketest 2>$null
    
    # Remove the Docker network
    docker network rm smoketest-network 2>$null
}

# Cleanup on script exit
trap {
    Write-Host "Error occurred. Cleaning up..." -ForegroundColor Red
    Cleanup
    exit 1
}

try {
    # Step 1: Build the project
    Write-Host "Step 1: Building the Azure Functions project..." -ForegroundColor Green
    dotnet build $projectDir -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE"
    }
    Write-Host "Build completed successfully." -ForegroundColor Green
    Write-Host ""

    # Step 2: Publish the project
    Write-Host "Step 2: Publishing the Azure Functions project..." -ForegroundColor Green
    if (Test-Path $publishDir) {
        Remove-Item $publishDir -Recurse -Force
    }
    dotnet publish $projectDir -c Release -o $publishDir
    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed with exit code $LASTEXITCODE"
    }
    Write-Host "Publish completed successfully." -ForegroundColor Green
    Write-Host ""

    # Step 3: Build Docker image
    Write-Host "Step 3: Building Docker image '$ImageName'..." -ForegroundColor Green
    docker build -t $ImageName $projectDir
    if ($LASTEXITCODE -ne 0) {
        throw "Docker build failed with exit code $LASTEXITCODE"
    }
    Write-Host "Docker image built successfully." -ForegroundColor Green
    Write-Host ""

    # Step 4: Create Docker network
    Write-Host "Step 4: Creating Docker network..." -ForegroundColor Green
    docker network create smoketest-network 2>$null
    Write-Host "Docker network created or already exists." -ForegroundColor Green
    Write-Host ""

    # Step 5: Start Azurite container
    Write-Host "Step 5: Starting Azurite storage emulator..." -ForegroundColor Green
    docker run -d `
        --name azurite-smoketest `
        --network smoketest-network `
        -p 10000:10000 `
        -p 10001:10001 `
        -p 10002:10002 `
        mcr.microsoft.com/azure-storage/azurite:latest
    
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to start Azurite container"
    }
    
    # Wait for Azurite to be ready
    Write-Host "Waiting for Azurite to be ready..." -ForegroundColor Yellow
    Start-Sleep -Seconds 5
    Write-Host "Azurite is ready." -ForegroundColor Green
    Write-Host ""

    # Step 6: Start Azure Functions container
    Write-Host "Step 6: Starting Azure Functions container..." -ForegroundColor Green
    
    # Azurite connection string for Docker network
    $storageConnectionString = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://azurite-smoketest:10000/devstoreaccount1;QueueEndpoint=http://azurite-smoketest:10001/devstoreaccount1;TableEndpoint=http://azurite-smoketest:10002/devstoreaccount1;"
    
    docker run -d `
        --name $ContainerName `
        --network smoketest-network `
        -p "${Port}:80" `
        -e AzureWebJobsStorage="$storageConnectionString" `
        -e FUNCTIONS_WORKER_RUNTIME=dotnet-isolated `
        $ImageName
    
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to start Functions container"
    }
    
    # Wait for Functions host to start
    Write-Host "Waiting for Azure Functions host to start..." -ForegroundColor Yellow
    $maxRetries = 30
    $retryCount = 0
    $isReady = $false
    
    while ($retryCount -lt $maxRetries) {
        Start-Sleep -Seconds 2
        $retryCount++
        
        try {
            $response = Invoke-WebRequest -Uri "http://localhost:$Port/admin/host/status" -UseBasicParsing -TimeoutSec 2 -ErrorAction SilentlyContinue
            if ($response.StatusCode -eq 200) {
                $isReady = $true
                break
            }
        }
        catch {
            # Continue retrying
        }
        
        Write-Host "." -NoNewline
    }
    
    Write-Host ""
    
    if (-not $isReady) {
        Write-Host "Functions host failed to start. Checking logs..." -ForegroundColor Red
        docker logs $ContainerName
        throw "Functions host did not start within the expected time"
    }
    
    Write-Host "Azure Functions host is ready." -ForegroundColor Green
    Write-Host ""

    # Step 7: Trigger orchestration
    Write-Host "Step 7: Triggering orchestration..." -ForegroundColor Green
    $startUrl = "http://localhost:$Port/api/HelloCitiesOrchestration_HttpStart"
    
    try {
        $startResponse = Invoke-WebRequest -Uri $startUrl -Method Post -UseBasicParsing
        if ($startResponse.StatusCode -ne 202) {
            throw "Unexpected status code: $($startResponse.StatusCode)"
        }
    }
    catch {
        Write-Host "Failed to trigger orchestration. Error: $_" -ForegroundColor Red
        Write-Host "Container logs:" -ForegroundColor Yellow
        docker logs $ContainerName
        throw
    }
    
    $responseContent = $startResponse.Content | ConvertFrom-Json
    $statusQueryGetUri = $responseContent.statusQueryGetUri
    $instanceId = $responseContent.id
    
    Write-Host "Orchestration started with instance ID: $instanceId" -ForegroundColor Green
    Write-Host "Status query URI: $statusQueryGetUri" -ForegroundColor Cyan
    Write-Host ""

    # Step 8: Poll for completion
    Write-Host "Step 8: Polling for orchestration completion..." -ForegroundColor Green
    $startTime = Get-Date
    $completed = $false
    
    while (((Get-Date) - $startTime).TotalSeconds -lt $Timeout) {
        Start-Sleep -Seconds 2
        
        try {
            $statusResponse = Invoke-WebRequest -Uri $statusQueryGetUri -UseBasicParsing
            $status = $statusResponse.Content | ConvertFrom-Json
            
            Write-Host "Current status: $($status.runtimeStatus)" -ForegroundColor Yellow
            
            if ($status.runtimeStatus -eq "Completed") {
                $completed = $true
                Write-Host ""
                Write-Host "Orchestration completed successfully!" -ForegroundColor Green
                Write-Host "Output: $($status.output)" -ForegroundColor Cyan
                break
            }
            elseif ($status.runtimeStatus -eq "Failed" -or $status.runtimeStatus -eq "Terminated") {
                throw "Orchestration ended with status: $($status.runtimeStatus)"
            }
        }
        catch {
            Write-Host "Error polling status: $_" -ForegroundColor Red
        }
    }
    
    if (-not $completed) {
        Write-Host "Container logs:" -ForegroundColor Yellow
        docker logs $ContainerName
        throw "Orchestration did not complete within timeout period"
    }
    
    Write-Host ""
    Write-Host "=== Smoke test completed successfully! ===" -ForegroundColor Green
}
finally {
    # Cleanup
    Cleanup
    
    # Cleanup publish directory
    if (Test-Path $publishDir) {
        Write-Host "Cleaning up publish directory..." -ForegroundColor Yellow
        Remove-Item $publishDir -Recurse -Force
    }
}

Write-Host ""
Write-Host "All smoke tests passed!" -ForegroundColor Green
exit 0
