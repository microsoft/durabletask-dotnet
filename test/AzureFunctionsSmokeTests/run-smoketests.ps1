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
    docker stop $ContainerName 2>$null | Out-Null
    docker rm $ContainerName 2>$null | Out-Null
    
    # Stop and remove Azurite container
    docker stop azurite-smoketest 2>$null | Out-Null
    docker rm azurite-smoketest 2>$null | Out-Null
    
    # Remove the Docker network
    docker network rm smoketest-network 2>$null | Out-Null
}

# Cleanup on script exit
trap {
    Write-Host "Error occurred. Cleaning up..." -ForegroundColor Red
    Cleanup
    exit 1
}

try {
    # Cleanup any existing containers first
    Write-Host "Cleaning up any existing containers..." -ForegroundColor Yellow
    Cleanup
    Write-Host ""
    
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
    # Using the default Azurite development account credentials
    $accountName = "devstoreaccount1"
    $accountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw=="
    $blobEndpoint = "http://azurite-smoketest:10000/$accountName"
    $queueEndpoint = "http://azurite-smoketest:10001/$accountName"
    $tableEndpoint = "http://azurite-smoketest:10002/$accountName"
    
    $storageConnectionString = @(
        "DefaultEndpointsProtocol=http"
        "AccountName=$accountName"
        "AccountKey=$accountKey"
        "BlobEndpoint=$blobEndpoint"
        "QueueEndpoint=$queueEndpoint"
        "TableEndpoint=$tableEndpoint"
    ) -join ";"
    
    docker run -d `
        --name $ContainerName `
        --network smoketest-network `
        -p "${Port}:80" `
        -e AzureWebJobsStorage="$storageConnectionString" `
        -e FUNCTIONS_WORKER_RUNTIME=dotnet-isolated `
        -e WEBSITE_HOSTNAME="localhost:$Port" `
        $ImageName
    
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to start Functions container"
    }
    
    # Wait for Functions host to start
    Write-Host "Waiting for Azure Functions host to start..." -ForegroundColor Yellow
    
    # Give the host time to fully initialize
    # The admin/host/status endpoint is not available in all configurations,
    # so we'll wait a reasonable amount of time and check logs
    Start-Sleep -Seconds 15
    
    # Check if the container is still running
    $containerStatus = docker inspect --format='{{.State.Status}}' $ContainerName
    if ($containerStatus -ne "running") {
        Write-Host "Functions container is not running. Checking logs..." -ForegroundColor Red
        docker logs $ContainerName
        throw "Functions container failed to start"
    }
    
    # Check logs for successful startup
    $logs = docker logs $ContainerName 2>&1 | Out-String
    if ($logs -match "Job host started" -or $logs -match "Host started") {
        Write-Host "Azure Functions host is ready." -ForegroundColor Green
    }
    else {
        Write-Host "Warning: Could not confirm host startup from logs." -ForegroundColor Yellow
        Write-Host "Attempting to continue with orchestration trigger..." -ForegroundColor Yellow
    }
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
    $consecutiveErrors = 0
    $maxConsecutiveErrors = 3
    
    while (((Get-Date) - $startTime).TotalSeconds -lt $Timeout) {
        Start-Sleep -Seconds 2
        
        try {
            $statusResponse = Invoke-WebRequest -Uri $statusQueryGetUri -UseBasicParsing
            $status = $statusResponse.Content | ConvertFrom-Json
            
            # Reset error counter on successful poll
            $consecutiveErrors = 0
            
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
            $consecutiveErrors++
            Write-Host "Error polling status (attempt $consecutiveErrors/$maxConsecutiveErrors): $_" -ForegroundColor Red
            
            if ($consecutiveErrors -ge $maxConsecutiveErrors) {
                Write-Host "Container logs:" -ForegroundColor Yellow
                docker logs $ContainerName
                throw "Too many consecutive errors polling orchestration status"
            }
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
