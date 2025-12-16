# Azure Functions Smoke Tests

This directory contains smoke tests for Azure Functions with Durable Task, designed to validate the SDK and Source Generator functionality in a real Azure Functions isolated .NET environment.

## Overview

The smoke tests ensure that:
- The Durable Task SDK works correctly with Azure Functions isolated worker
- Source generators produce valid code
- Orchestrations can be triggered and completed successfully
- The complete end-to-end workflow functions as expected

## Structure

- **HelloCitiesOrchestration.cs** - Simple orchestration that calls multiple activities
- **Program.cs** - Azure Functions host entry point
- **host.json** - Azure Functions host configuration
- **local.settings.json** - Local development settings
- **Dockerfile** - Docker image configuration for the Functions app
- **run-smoketests.ps1** - PowerShell script to run smoke tests locally or in CI

## Running Smoke Tests Locally

### Prerequisites

- Docker installed and running
- PowerShell Core (pwsh) installed
- .NET 8.0 SDK or later

### Run the Tests

From the `test/AzureFunctionsSmokeTests` directory:

```bash
pwsh -File run-smoketests.ps1
```

The script will:
1. Build and publish the Azure Functions project
2. Create a Docker image
3. Start Azurite (Azure Storage emulator) in a Docker container
4. Start the Azure Functions app in a Docker container
5. Trigger the HelloCities orchestration via HTTP
6. Poll for orchestration completion
7. Validate the result
8. Clean up all containers

### Parameters

The script accepts the following optional parameters:

```powershell
pwsh -File run-smoketests.ps1 `
    -ImageName "custom-image-name" `
    -ContainerName "custom-container-name" `
    -Port 8080 `
    -Timeout 120
```

## CI Integration

The smoke tests are automatically run in GitHub Actions via the `.github/workflows/azure-functions-smoke-tests.yml` workflow on:
- Push to `main` or `feature/**` branches
- Pull requests targeting `main` or `feature/**` branches
- Manual workflow dispatch

## Troubleshooting

If the smoke tests fail:

1. **Check container logs**: The script will display logs automatically on failure
2. **Verify Azurite is running**: Ensure port 10000-10002 are available
3. **Check Functions app port**: Ensure the configured port (default 8080) is available
4. **Build errors**: Ensure all dependencies are restored with `dotnet restore`

## Adding New Smoke Tests

To add new orchestration scenarios:

1. Create new function classes following the pattern in `HelloCitiesOrchestration.cs`
2. Ensure proper XML documentation comments
3. Add test logic to validate the new scenario
4. Update this README with the new test case
