#!/usr/bin/env bash
set -euo pipefail

NAMESPACE="${NAMESPACE:-batch-inference}"

echo ">>> [Postgres] Applying secrets / PVC / deployment / service..."
kubectl apply -n "${NAMESPACE}" -f k8s/postgres/postgres-secret.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/postgres/postgres-pvc.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/postgres/postgres-deployment.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/postgres/postgres-service.yaml

echo ">>> [Postgres] Restarting deployment..."
kubectl rollout restart deployment/postgres -n "${NAMESPACE}"
kubectl rollout status deployment/postgres -n "${NAMESPACE}"

echo ">>> [Postgres] Redeploy complete."