#!/bin/bash
# Simple script to clear all firing alerts by deleting all silences
# Alerts will automatically resolve when their conditions are no longer met

set -e

NAMESPACE="batch-inference"
ALERTMANAGER_POD=$(kubectl get pods -n $NAMESPACE -l app=alertmanager -o jsonpath='{.items[0].metadata.name}')

if [ -z "$ALERTMANAGER_POD" ]; then
    echo "Error: Alertmanager pod not found in namespace $NAMESPACE"
    exit 1
fi

echo "Clearing all alert silences..."

# Get all silences
SILENCES=$(kubectl exec -n $NAMESPACE $ALERTMANAGER_POD -- wget -qO- http://localhost:9093/api/v2/silences 2>/dev/null)

# Extract silence IDs and delete them
SILENCE_IDS=$(echo "$SILENCES" | grep -o '"id":"[^"]*"' | sed 's/"id":"\([^"]*\)"/\1/' || true)

if [ -z "$SILENCE_IDS" ]; then
    echo "✓ No silences found. Alerts will resolve automatically when conditions are met."
    exit 0
fi

# Delete each silence
DELETED=0
for SILENCE_ID in $SILENCE_IDS; do
    if kubectl exec -n $NAMESPACE $ALERTMANAGER_POD -- wget -qO- --method=DELETE http://localhost:9093/api/v2/silence/$SILENCE_ID >/dev/null 2>&1; then
        DELETED=$((DELETED + 1))
    fi
done

echo "✓ Deleted $DELETED silence(s)"
echo ""
echo "Alerts will now automatically resolve when their conditions are no longer met."
echo "Check Prometheus UI to see alert status: kubectl port-forward -n $NAMESPACE svc/prometheus 9090:9090"
