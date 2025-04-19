# Get the current directory and file path
$SCRIPT_DIR = $PSScriptRoot
$LAUNCH_SETTINGS = Join-Path $SCRIPT_DIR "Properties\launchSettings.json"

# Read the current content
$content = Get-Content $LAUNCH_SETTINGS -Raw

# Extract current port from profiles/http section
$portMatch = [regex]::Match($content, '"applicationUrl":\s*"http://localhost:(\d+)"')
$CURRENT_PORT = [int]$portMatch.Groups[1].Value

# Extract current taskhub number
$taskhubMatch = [regex]::Match($content, 'TaskHub=th(\d+)')
$CURRENT_TASKHUB_NUM = [int]$taskhubMatch.Groups[1].Value

# Calculate next port (5010-5014)
if ($CURRENT_PORT -eq 5014) {
    $NEXT_PORT = 5010
} else {
    $NEXT_PORT = $CURRENT_PORT + 1
}

# Increment taskhub number
$NEXT_TASKHUB = "th" + ($CURRENT_TASKHUB_NUM + 1)

# Update the content with new port and taskhub
$content = $content -replace '"applicationUrl":\s*"http://localhost:\d+"', "`"applicationUrl`": `"http://localhost:$NEXT_PORT`""
$content = $content -replace "TaskHub=th\d+", "TaskHub=$NEXT_TASKHUB"

# Write the changes back to the file
$content | Set-Content $LAUNCH_SETTINGS

Write-Host "Updated port to $NEXT_PORT and taskhub to $NEXT_TASKHUB" 