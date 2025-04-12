#!/bin/bash

# Set the base directory
BASE_DIR="$(pwd)"
INSTANCE_COUNT=20000
BASE_URL="http://localhost:5008"
LOG_FILE="test_data_generator.log"

# Function to log messages
log() {
    echo "$(date '+%Y-%m-%d %H:%M:%S') - $1" | tee -a "$LOG_FILE"
}

# Function to run the web app in background
run_web_app() {
    local task_hub=$1
    log "Starting web application for TaskHub: $task_hub"
    
    # Kill any existing process
    if [ ! -z "$WEB_APP_PID" ]; then
        kill $WEB_APP_PID 2>/dev/null || true
    fi
    
    # Set the connection string
    export DURABLE_TASK_SCHEDULER_CONNECTION_STRING="Endpoint=https://wbret01-fhcvf6h3dbck.eastus2.durabletask.dev;TaskHub=$task_hub;Authentication=DefaultAzure"
    
    # Start the web app
    dotnet run &
    WEB_APP_PID=$!
    sleep 10 # Wait for the app to start
}

# Function to create orchestration instances
create_instances() {
    local task_hub=$1
    log "Creating $INSTANCE_COUNT instances for TaskHub: $task_hub"
    
    # Create instances using curl
    curl -X POST "$BASE_URL/instances?count=$INSTANCE_COUNT" \
         -H "Content-Type: application/json" \
         -d '{"message": "Test input for orchestration"}' \
         -s -o /dev/null -w "%{http_code}" | tee -a "$LOG_FILE"
    
    if [ $? -ne 0 ]; then
        log "Error creating instances for TaskHub: $task_hub"
        return 1
    fi
    
    log "Successfully created instances for TaskHub: $task_hub"
    return 0
}

# Main execution
log "Starting test data generation process..."

# Loop through task hub names from thh1 to thh150
for i in $(seq 1 150); do
    task_hub="thh$i"
    log "Processing TaskHub: $task_hub"
    
    # Start web app with new task hub
    run_web_app "$task_hub"
    
    # Create instances
    if ! create_instances "$task_hub"; then
        log "Failed to create instances for $task_hub, continuing with next TaskHub..."
    fi
    
    # Kill the web app before moving to next task hub
    log "Stopping web application for TaskHub: $task_hub"
    kill $WEB_APP_PID
done

log "Process finished. Check $LOG_FILE for details." 