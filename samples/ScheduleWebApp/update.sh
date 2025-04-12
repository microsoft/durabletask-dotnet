#!/bin/bash

# Get the current directory
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
LAUNCH_SETTINGS="$SCRIPT_DIR/Properties/launchSettings.json"

# Read current port and taskhub
CURRENT_PORT=$(grep -o '"applicationUrl": "http://localhost:[0-9]*"' "$LAUNCH_SETTINGS" | grep -o '[0-9]*')
CURRENT_TASKHUB=$(grep -o 'TaskHub=thh[0-9]*' "$LAUNCH_SETTINGS" | grep -o 'thh[0-9]*')

# Calculate next port (5010-5014)
if [ "$CURRENT_PORT" -eq 5014 ]; then
    NEXT_PORT=5010
else
    NEXT_PORT=$((CURRENT_PORT + 1))
fi

# Calculate next taskhub (thh11-thh12)
if [ "$CURRENT_TASKHUB" = "thh11" ]; then
    NEXT_TASKHUB="thh12"
else
    NEXT_TASKHUB="thh11"
fi

# Update the file with new port and taskhub
sed -i "s/\"applicationUrl\": \"http:\/\/localhost:[0-9]*\"/\"applicationUrl\": \"http:\/\/localhost:$NEXT_PORT\"/g" "$LAUNCH_SETTINGS"
sed -i "s/TaskHub=$CURRENT_TASKHUB/TaskHub=$NEXT_TASKHUB/g" "$LAUNCH_SETTINGS"

echo "Updated port to $NEXT_PORT and taskhub to $NEXT_TASKHUB"
