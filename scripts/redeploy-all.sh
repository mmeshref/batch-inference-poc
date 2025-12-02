#!/usr/bin/env bash
set -euo pipefail

TAG="${1:-v-dev}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo ">>> Redeploying EVERYTHING with tag: ${TAG}"

# Infra: namespace + PVC
"${SCRIPT_DIR}/redeploy-infra.sh"

# Data layer
"${SCRIPT_DIR}/redeploy-postgres.sh"

# Monitoring stack
"${SCRIPT_DIR}/redeploy-prometheus.sh"
"${SCRIPT_DIR}/redeploy-alertmanager.sh"
"${SCRIPT_DIR}/redeploy-grafana.sh"

# Core services
"${SCRIPT_DIR}/redeploy-api-gateway.sh" "${TAG}"
"${SCRIPT_DIR}/redeploy-scheduler.sh" "${TAG}"
"${SCRIPT_DIR}/redeploy-gpu-workers.sh" "${TAG}"
"${SCRIPT_DIR}/redeploy-portal.sh" "${TAG}"

echo ">>> All components redeployed with tag: ${TAG}"