#!/bin/bash

URL="https://wbdtsdk.wonderfulsmoke-2333f019.uksouth.azurecontainerapps.io/instances?count=2000"

while true; do
  for i in {1..5}; do
    curl -X POST "$URL" &
  done
  wait  # Wait for all 5 to complete before next second
  sleep 1
done
