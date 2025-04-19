# Configuration
$basePort = 5012
$endPort = 5012
$instanceCount = 100000  # 2000 instances divided by 5 ports
$baseUrlTemplate = "http://localhost:{0}"
$requestBody = @{
    message = "Test input for orchestration"
} | ConvertTo-Json

# Create a list of jobs
$jobs = @()

# Start concurrent requests
for ($port = $basePort; $port -le $endPort; $port++) {
    $url = $baseUrlTemplate -f $port
    $fullUrl = "$url/instances?count=$instanceCount"
    
    $job = Start-Job -ScriptBlock {
        param($url, $body)
        $headers = @{
            "Content-Type" = "application/json"
        }
        $maxRetries = 3
        $retryCount = 0
        $success = $false
        
        while (-not $success -and $retryCount -lt $maxRetries) {
            try {
                $response = Invoke-RestMethod -Uri $url -Method Post -Body $body -Headers $headers
                Write-Output "Successfully created instances on $url"
                $success = $true
            }
            catch {
                $retryCount++
                if ($retryCount -ge $maxRetries) {
                    Write-Output "Error creating instances on $url after $maxRetries attempts: $_"
                } else {
                    Write-Output "Error creating instances on $url (attempt $retryCount of $maxRetries): $_. Retrying..."
                    Start-Sleep -Seconds   # Add a small delay before retrying
                }
            }
        }
    } -ArgumentList $fullUrl, $requestBody
    
    $jobs += $job
}

# Wait for all jobs to complete
Write-Host "Waiting for all requests to complete..."
$jobs | Wait-Job | Receive-Job

# Clean up jobs
$jobs | Remove-Job

Write-Host "All requests completed!" 