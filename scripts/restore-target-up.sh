#!/bin/bash
# Script to restore a target back up - alerts will automatically resolve
# Usage: ./restore-target-up.sh [api-gateway|scheduler|gpu-worker-spot|gpu-worker-dedicated|all]

set -e

NAMESPACE="batch-inference"
SERVICE="${1:-all}"

# Get default replica count for a service (bash 3.x compatible)
get_replica_count() {
    case "$1" in
        api-gateway)
            echo "1"
            ;;
        scheduler)
            echo "1"
            ;;
        gpu-worker-spot)
            echo "15"
            ;;
        gpu-worker-dedicated)
            echo "1"
            ;;
        *)
            echo ""
            ;;
    esac
}

if [ "$SERVICE" = "all" ]; then
    echo "Restoring all services to normal state..."
    SERVICES=("api-gateway" "scheduler" "gpu-worker-spot" "gpu-worker-dedicated")
    for svc in "${SERVICES[@]}"; do
        REPLICA_COUNT=$(get_replica_count "$svc")
        echo "  Scaling $svc to $REPLICA_COUNT replicas..."
        if kubectl scale deployment $svc -n $NAMESPACE --replicas=$REPLICA_COUNT 2>/dev/null; then
            echo "    Waiting for $svc pods to be ready..."
            kubectl wait --for=condition=ready pod -l app=$svc -n $NAMESPACE --timeout=120s 2>/dev/null || echo "    ⚠ Warning: Some $svc pods may not be ready yet"
        else
            echo "    (deployment $svc not found, skipping)"
        fi
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

    REPLICA_COUNT=$(get_replica_count "$SERVICE")
    if [ -z "$REPLICA_COUNT" ]; then
        echo "Error: Unknown default replica count for $SERVICE"
        exit 1
    fi

    echo "Restoring $SERVICE to normal state..."
    echo "Scaling deployment to $REPLICA_COUNT replicas..."
    
    kubectl scale deployment $SERVICE -n $NAMESPACE --replicas=$REPLICA_COUNT

    echo "Waiting for pods to be ready..."
    kubectl wait --for=condition=ready pod -l app=$SERVICE -n $NAMESPACE --timeout=120s 2>/dev/null || {
        echo "⚠ Warning: Some pods may not be ready yet. Checking status..."
        kubectl get pods -l app=$SERVICE -n $NAMESPACE
    }

    echo "✓ $SERVICE scaled up to $REPLICA_COUNT replicas and pods are ready"
fi

echo ""
echo "Waiting for Prometheus to detect targets are up..."
echo "Prometheus scrape interval is 5s, so targets should be detected within ~10-15 seconds"
echo ""
echo "Alerts will automatically resolve once Prometheus sees targets as up (up=1)"
echo "This typically happens within 30-60 seconds after pods are ready"
echo ""
echo "To check alert status:"
echo "  kubectl port-forward -n $NAMESPACE svc/prometheus 9090:9090"
echo "  Then open http://localhost:9090/alerts"
echo ""
echo "To verify targets are up in Prometheus:"
echo "  Query: up{job=\"$SERVICE\"} or up{instance=\"$SERVICE:80\"}"
echo ""
echo "Note: Alerts will automatically transition from 'firing' to 'resolved' when conditions are met."
