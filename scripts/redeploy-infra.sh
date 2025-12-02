#!/usr/bin/env bash
set -euo pipefail

NAMESPACE="${NAMESPACE:-batch-inference}"

echo ">>> [Infra] Applying namespace..."
kubectl apply -f k8s/namespace.yaml

echo ">>> [Infra] Applying storage PVC..."
kubectl apply -n "${NAMESPACE}" -f k8s/storage/user-storage-pvc.yaml

echo ">>> [Infra] Infra apply complete."