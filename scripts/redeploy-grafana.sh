#!/usr/bin/env bash
set -euo pipefail

NAMESPACE="batch-inference"

echo ">>> [Grafana] Deploying / Redeploying Grafana into namespace: ${NAMESPACE}"

# Ensure namespace exists
if ! kubectl get ns "${NAMESPACE}" >/dev/null 2>&1; then
  echo ">>> [Grafana] Namespace ${NAMESPACE} does not exist. Creating..."
  kubectl create namespace "${NAMESPACE}"
fi

apply_if_exists() {
  local file="$1"
  if [ -f "$file" ]; then
    echo ">>> [Grafana] Applying ${file}..."
    kubectl apply -f "$file" -n "${NAMESPACE}"
  else
    echo ">>> [Grafana] SKIP: ${file} not found, skipping."
  fi
}

# Apply ConfigMaps (datasources + dashboards)
apply_if_exists "k8s/monitoring/grafana-dashboard-providers.yaml"
apply_if_exists "k8s/monitoring/grafana-datasource-config.yaml"
apply_if_exists "k8s/monitoring/grafana-dashboards.yaml"

# Apply Deployment and Service
apply_if_exists "k8s/monitoring/grafana-deployment.yaml"
apply_if_exists "k8s/monitoring/grafana-service.yaml"

echo ">>> [Grafana] Waiting for grafana deployment to roll out..."
if kubectl get deployment grafana -n "${NAMESPACE}" >/dev/null 2>&1; then
  kubectl rollout status deployment/grafana -n "${NAMESPACE}"
else
  echo ">>> [Grafana] WARNING: deployment 'grafana' not found in ${NAMESPACE}."
fi

echo ">>> [Grafana] Done."