#!/usr/bin/env bash

set -euo pipefail
# One tag for all components, default v-dev
TAG="${1:-v-dev}"
# Resolve script directory so we can call sibling scripts reliably
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo ">>> Redeploying all services with tag: ${TAG}"
"${SCRIPT_DIR}/redeploy-api-gateway.sh" "${TAG}"
"${SCRIPT_DIR}/redeploy-scheduler.sh" "${TAG}"
"${SCRIPT_DIR}/redeploy-gpu-worker.sh" "${TAG}"
"${SCRIPT_DIR}/redeploy-batch-portal.sh" "${TAG}"

echo ">>> All services redeployed with tag: ${TAG}"

