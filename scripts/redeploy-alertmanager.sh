#!/usr/bin/env bash
set -euo pipefail

NAMESPACE="${NAMESPACE:-doubleword-batch}"

echo ">>> [Alertmanager] Applying config + deployment + service..."
kubectl apply -n "${NAMESPACE}" -f k8s/monitoring/alerting/alertmanager-config.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/monitoring/alerting/alertmanager.yaml

echo ">>> [Alertmanager] Restarting deployment..."
kubectl rollout restart deployment/alertmanager -n "${NAMESPACE}"
kubectl rollout status deployment/alertmanager -n "${NAMESPACE}"

echo ">>> [Alertmanager] Redeploy complete."