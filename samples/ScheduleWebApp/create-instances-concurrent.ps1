# Configuration
$basePort = 5010
$endPort = 5014
$instanceCount = 2000  # 2000 instances divided by 5 ports
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
        try {
            $response = Invoke-RestMethod -Uri $url -Method Post -Body $body -Headers $headers
            Write-Output "Successfully created instances on $url"
        }
        catch {
            Write-Output "Error creating instances on $url : $_"
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