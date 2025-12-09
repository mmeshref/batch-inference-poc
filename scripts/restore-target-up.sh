#!/bin/bash
# Script to restore a target back up - alerts will automatically resolve
# Usage: ./restore-target-up.sh [api-gateway|scheduler|gpu-worker-spot|gpu-worker-dedicated|all]

set -e

NAMESPACE="batch-inference"
SERVICE="${1:-all}"

# Default replica counts
declare -A REPLICAS=(
    ["api-gateway"]="1"
    ["scheduler"]="1"
    ["gpu-worker-spot"]="15"
    ["gpu-worker-dedicated"]="1"
)

if [ "$SERVICE" = "all" ]; then
    echo "Restoring all services to normal state..."
    for svc in "${!REPLICAS[@]}"; do
        echo "  Scaling $svc to ${REPLICAS[$svc]} replicas..."
        kubectl scale deployment $svc -n $NAMESPACE --replicas=${REPLICAS[$svc]} 2>/dev/null || echo "    (deployment $svc not found, skipping)"
    done
    echo ""
    echo "✓ All services restored"
else
    # Validate service name
    VALID_SERVICES=("api-gateway" "scheduler" "gpu-worker-spot" "gpu-worker-dedicated")
    if [[ ! " ${VALID_SERVICES[@]} " =~ " ${SERVICE} " ]]; then
        echo "Error: Invalid service '$SERVICE'"
        echo "Usage: $0 [api-gateway|scheduler|gpu-worker-spot|gpu-worker-dedicated|all]"
        exit 1
    fi

    REPLICA_COUNT=${REPLICAS[$SERVICE]}
    if [ -z "$REPLICA_COUNT" ]; then
        echo "Error: Unknown default replica count for $SERVICE"
        exit 1
    fi

    echo "Restoring $SERVICE to normal state..."
    echo "Scaling deployment to $REPLICA_COUNT replicas..."
    
    kubectl scale deployment $SERVICE -n $NAMESPACE --replicas=$REPLICA_COUNT

    echo "✓ $SERVICE scaled up to $REPLICA_COUNT replicas"
fi

echo ""
echo "Waiting for targets to come back up..."
echo "Alerts should automatically resolve once targets are healthy (usually within 1-2 minutes)"
echo ""
echo "To check alert status:"
echo "  kubectl port-forward -n $NAMESPACE svc/prometheus 9090:9090"
echo "  Then open http://localhost:9090/alerts"
echo ""
echo "Note: Alerts will automatically transition from 'firing' to 'resolved' when conditions are met."
