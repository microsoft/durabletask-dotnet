# # Simple script to list NuGet packages
# $packages = @(
#     "Microsoft.DurableTask.Abstractions",
#     "Microsoft.DurableTask.Client", 
#     "Microsoft.DurableTask.Worker",
#     "Microsoft.DurableTask.Grpc",
#     "Microsoft.DurableTask.Client.Grpc",
#     "Microsoft.DurableTask.Worker.Grpc",
#     "Microsoft.DurableTask.Client.OrchestrationServiceClientShim",
#     "Microsoft.DurableTask.Extensions.AzureBlobPayloads",
#     "Microsoft.DurableTask.Client.AzureManaged",
#     "Microsoft.DurableTask.Worker.AzureManaged", 
#     "Microsoft.DurableTask.ScheduledTasks"
# )

# Write-Host "DurableTask .NET NuGet Packages:" -ForegroundColor Green
# Write-Host ""

# foreach ($package in $packages) {
#     $url = "https://www.nuget.org/packages/$package"
#     Write-Host "- $package" -ForegroundColor White
#     Write-Host "  URL: $url" -ForegroundColor Gray
#     Write-Host ""
# }


# $packages = @(
#     "Microsoft.DurableTask.Abstractions",
#     "Microsoft.DurableTask.Client",
#     "Microsoft.DurableTask.Worker",
#     "Microsoft.DurableTask.Grpc",
#     "Microsoft.DurableTask.Client.Grpc",
#     "Microsoft.DurableTask.Worker.Grpc",
#     "Microsoft.DurableTask.Client.OrchestrationServiceClientShim",
#     "Microsoft.DurableTask.Extensions.AzureBlobPayloads",
#     "Microsoft.DurableTask.Client.AzureManaged",
#     "Microsoft.DurableTask.Worker.AzureManaged",
#     "Microsoft.DurableTask.ScheduledTasks"
# )

# Write-Host "DurableTask .NET NuGet Packages (latest versions):" -ForegroundColor Green
# Write-Host ""

# foreach ($package in $packages) {
#     $lowerName = $package.ToLower()
#     $metadataUrl = "https://api.nuget.org/v3/registration5-semver1/$lowerName/index.json"
#     try {
#         $metadata = Invoke-RestMethod -Uri $metadataUrl -ErrorAction Stop
#         $lastPage = $metadata.items[-1]
#         $lastVersionEntry = $lastPage.items[-1]
#         $latestVersion = $lastVersionEntry.catalogEntry.version
#         $packageUrl = "https://www.nuget.org/packages/$package/$latestVersion"
#         Write-Host "- $package" -ForegroundColor White
#         Write-Host "  Latest version: $latestVersion" -ForegroundColor Yellow
#         Write-Host "  URL: $packageUrl" -ForegroundColor Gray
#         Write-Host ""
#     }
#     catch {
#         Write-Host "- $package" -ForegroundColor White
#         Write-Host "  ERROR retrieving metadata: $($_.Exception.Message)" -ForegroundColor Red
#         Write-Host ""
#     }
# }


$packages = @(
    "Microsoft.DurableTask.Abstractions",
    "Microsoft.DurableTask.Client",
    "Microsoft.DurableTask.Worker",
    "Microsoft.DurableTask.Grpc",
    "Microsoft.DurableTask.Client.Grpc",
    "Microsoft.DurableTask.Worker.Grpc",
    "Microsoft.DurableTask.Client.OrchestrationServiceClientShim",
    "Microsoft.DurableTask.Extensions.AzureBlobPayloads",
    "Microsoft.DurableTask.Client.AzureManaged",
    "Microsoft.DurableTask.Worker.AzureManaged",
    "Microsoft.DurableTask.ScheduledTasks",
    "Microsoft.DurableTask.ExportHistory"
)

Write-Host "DurableTask .NET NuGet Packages (latest versions):" -ForegroundColor Green
Write-Host ""

foreach ($package in $packages) {
    $lowerName = $package.ToLower()
    $metadataUrl = "https://api.nuget.org/v3/registration5-semver1/$lowerName/index.json"
    try {
        $metadata = Invoke-RestMethod -Uri $metadataUrl -ErrorAction Stop
        $lastPage = $metadata.items[-1]
        $lastVersionEntry = $lastPage.items[-1]
        $latestVersion = $lastVersionEntry.catalogEntry.version
        $packageUrl = "https://www.nuget.org/packages/$package/$latestVersion"
        Write-Host "- $package" -ForegroundColor White
        Write-Host "  Latest version: $latestVersion" -ForegroundColor Yellow
        Write-Host "  URL: $packageUrl" -ForegroundColor Gray
        Write-Host ""
    }
    catch {
        $fallbackUrl = "https://www.nuget.org/packages/$package"
        Write-Host "- $package" -ForegroundColor White
        Write-Host "  Version: (not found or preview-only)" -ForegroundColor DarkYellow
        Write-Host "  URL: $fallbackUrl" -ForegroundColor Gray
        Write-Host ""
    }
}
