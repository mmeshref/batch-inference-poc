#!/bin/bash
# Script to clear/silence firing alerts in Alertmanager

set -e

NAMESPACE="batch-inference"
ALERTMANAGER_SERVICE="alertmanager"
ALERTMANAGER_PORT="9093"

# Get Alertmanager pod name
POD_NAME=$(kubectl get pods -n $NAMESPACE -l app=alertmanager -o jsonpath='{.items[0].metadata.name}')

if [ -z "$POD_NAME" ]; then
    echo "Error: Alertmanager pod not found in namespace $NAMESPACE"
    exit 1
fi

echo "Alertmanager pod: $POD_NAME"
echo ""
echo "Options to clear alerts:"
echo ""
echo "1. View current firing alerts:"
echo "   kubectl exec -n $NAMESPACE $POD_NAME -- wget -qO- http://localhost:9093/api/v2/alerts"
echo ""
echo "2. Access Alertmanager UI via port-forward:"
echo "   kubectl port-forward -n $NAMESPACE svc/$ALERTMANAGER_SERVICE 9093:9093"
echo "   Then open: http://localhost:9093"
echo ""
echo "3. Silence a specific alert via API (example for TargetDown):"
echo "   kubectl exec -n $NAMESPACE $POD_NAME -- wget -qO- --post-data='{\"matchers\":[{\"name\":\"alertname\",\"value\":\"TargetDown\",\"isRegex\":false}],\"startsAt\":\"'$(date -u +%Y-%m-%dT%H:%M:%S.000Z)'\",\"endsAt\":\"'$(date -u -d '+1 hour' +%Y-%m-%dT%H:%M:%S.000Z)'\",\"comment\":\"Silencing alert for testing\",\"createdBy\":\"admin\"}' --header='Content-Type: application/json' http://localhost:9093/api/v2/silences"
echo ""
echo "4. List all silences:"
echo "   kubectl exec -n $NAMESPACE $POD_NAME -- wget -qO- http://localhost:9093/api/v2/silences"
echo ""
echo "5. Delete a silence (replace SILENCE_ID):"
echo "   kubectl exec -n $NAMESPACE $POD_NAME -- wget -qO- --method=DELETE http://localhost:9093/api/v2/silence/SILENCE_ID"
echo ""

# Interactive mode: show current alerts
echo "=== Current Firing Alerts ==="
kubectl exec -n $NAMESPACE $POD_NAME -- wget -qO- http://localhost:9093/api/v2/alerts 2>/dev/null | python3 -m json.tool 2>/dev/null || echo "Unable to parse alerts (may need to install python3)"
