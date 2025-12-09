#!/bin/bash
# Quick script to silence TargetDown alerts

set -e

NAMESPACE="batch-inference"
ALERTMANAGER_POD=$(kubectl get pods -n $NAMESPACE -l app=alertmanager -o jsonpath='{.items[0].metadata.name}')

if [ -z "$ALERTMANAGER_POD" ]; then
    echo "Error: Alertmanager pod not found"
    exit 1
fi

# Calculate timestamps
START_TIME=$(date -u +%Y-%m-%dT%H:%M:%S.000Z)
END_TIME=$(date -u -d '+1 hour' +%Y-%m-%dT%H:%M:%S.000Z)

# Create silence for TargetDown alert
echo "Creating silence for TargetDown alert..."
SILENCE_RESPONSE=$(kubectl exec -n $NAMESPACE $ALERTMANAGER_POD -- sh -c "
  wget -qO- --post-data='{
    \"matchers\": [{
      \"name\": \"alertname\",
      \"value\": \"TargetDown\",
      \"isRegex\": false
    }],
    \"startsAt\": \"$START_TIME\",
    \"endsAt\": \"$END_TIME\",
    \"comment\": \"Silencing TargetDown alert - service restored\",
    \"createdBy\": \"admin\"
  }' --header='Content-Type: application/json' http://localhost:9093/api/v2/silences
" 2>/dev/null)

if [ $? -eq 0 ]; then
    echo "✓ Silence created successfully"
    echo "Response: $SILENCE_RESPONSE"
    echo ""
    echo "To view silences: kubectl exec -n $NAMESPACE $ALERTMANAGER_POD -- wget -qO- http://localhost:9093/api/v2/silences"
    echo "To delete this silence, extract the silence ID from the response above and run:"
    echo "  kubectl exec -n $NAMESPACE $ALERTMANAGER_POD -- wget -qO- --method=DELETE http://localhost:9093/api/v2/silence/<SILENCE_ID>"
else
    echo "Error: Failed to create silence"
    exit 1
fi
