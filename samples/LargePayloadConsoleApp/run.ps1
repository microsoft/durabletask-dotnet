Param(
    [Parameter(Mandatory = $true)]
    [string]$SchedulerConnectionString,

    [string]$StorageConnectionString = "UseDevelopmentStorage=true",

    [string]$PayloadContainer = "durabletask-payloads",

    [switch]$StartAzurite,

    [switch]$VerboseLogging
)

$ErrorActionPreference = "Stop"

function Write-Info($msg) {
    Write-Host "[info] $msg"
}

function Start-AzuriteDocker {
    param(
        [string]$ContainerName = "durabletask-azurite"
    )

    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        Write-Info "Docker not found; skipping Azurite startup."
        return $false
    }

    try {
        $existing = (docker ps -a --filter "name=$ContainerName" --format "{{.ID}}")
        if ($existing) {
            Write-Info "Starting existing Azurite container '$ContainerName'..."
            docker start $ContainerName | Out-Null
            return $true
        }

        Write-Info "Launching Azurite in Docker as '$ContainerName' on ports 10000-10002..."
        docker run -d -p 10000:10000 -p 10001:10001 -p 10002:10002 --name $ContainerName mcr.microsoft.com/azure-storage/azurite | Out-Null
        Start-Sleep -Seconds 2
        return $true
    }
    catch {
        Write-Warning "Failed to start Azurite via Docker: $_"
        return $false
    }
}

try {
    # Set required/optional environment variables for the sample
    $env:DURABLE_TASK_SCHEDULER_CONNECTION_STRING = $SchedulerConnectionString
    $env:DURABLETASK_STORAGE = $StorageConnectionString
    $env:DURABLETASK_PAYLOAD_CONTAINER = $PayloadContainer

    Write-Info "DURABLE_TASK_SCHEDULER_CONNECTION_STRING is set."
    Write-Info "DURABLETASK_STORAGE = '$($env:DURABLETASK_STORAGE)'"
    Write-Info "DURABLETASK_PAYLOAD_CONTAINER = '$($env:DURABLETASK_PAYLOAD_CONTAINER)'"

    if ($StartAzurite) {
        $started = Start-AzuriteDocker
        if ($started) {
            Write-Info "Azurite is running (Docker)."
        }
    }

    $projectPath = Join-Path $PSScriptRoot "LargePayloadConsoleApp.csproj"
    if (-not (Test-Path $projectPath)) {
        throw "Project file not found at $projectPath"
    }

    Write-Info "Running sample..."
    $argsList = @("run", "--project", $projectPath)
    if ($VerboseLogging) { $argsList += @("-v", "detailed") }

    & dotnet @argsList
}
catch {
    Write-Error $_
    exit 1
}

