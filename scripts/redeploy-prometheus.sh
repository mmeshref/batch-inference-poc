#!/usr/bin/env bash
set -euo pipefail

NAMESPACE="${NAMESPACE:-batch-inference}"

echo ">>> [Prometheus] Applying ConfigMap + alert rules + deployment + service..."
kubectl apply -n "${NAMESPACE}" -f k8s/monitoring/prometheus-configmap.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/monitoring/alert-rules.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/monitoring/prometheus-deployment.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/monitoring/prometheus-service.yaml

echo ">>> [Prometheus] Restarting deployment..."
kubectl rollout restart deployment/prometheus -n "${NAMESPACE}"
kubectl rollout status deployment/prometheus -n "${NAMESPACE}"

echo ">>> [Prometheus] Redeploy complete."