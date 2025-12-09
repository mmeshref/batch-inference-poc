#!/bin/bash
# Script to simulate a target going down to trigger alerts
# Usage: ./simulate-target-down.sh [api-gateway|scheduler|gpu-worker-spot|gpu-worker-dedicated]

set -e

NAMESPACE="batch-inference"
SERVICE="${1:-api-gateway}"

# Validate service name
VALID_SERVICES=("api-gateway" "scheduler" "gpu-worker-spot" "gpu-worker-dedicated")
if [[ ! " ${VALID_SERVICES[@]} " =~ " ${SERVICE} " ]]; then
    echo "Error: Invalid service '$SERVICE'"
    echo "Usage: $0 [api-gateway|scheduler|gpu-worker-spot|gpu-worker-dedicated]"
    exit 1
fi

echo "Simulating target down for: $SERVICE"
echo "Scaling deployment to 0 replicas..."

# Scale down the deployment
kubectl scale deployment $SERVICE -n $NAMESPACE --replicas=0

echo "✓ $SERVICE scaled down to 0 replicas"
echo ""
echo "Waiting for Prometheus to detect the target is down..."
echo "Alerts should fire after ~1 minute (TargetDown alert threshold)"
echo ""
echo "To check alerts:"
echo "  kubectl port-forward -n $NAMESPACE svc/prometheus 9090:9090"
echo "  Then open http://localhost:9090/alerts"
echo ""
echo "To restore the service:"
echo "  ./scripts/restore-target-up.sh $SERVICE"
