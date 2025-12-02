#!/usr/bin/env bash

set -euo pipefail
NAMESPACE="${NAMESPACE:-doubleword-batch}"
TAG="${1:-v-dev}"
IMAGE="dwb/batch-portal:${TAG}"

echo ">>> Building BatchPortal image: ${IMAGE}"
docker build -f docker/BatchPortal.Dockerfile -t "${IMAGE}" .

echo ">>> [Portal] Applying deployment + service manifests..."
kubectl apply -n "${NAMESPACE}" -f k8s/portal/batch-portal.yaml

echo ">>> Updating Kubernetes deployment/batch-portal in namespace ${NAMESPACE} to image ${IMAGE}"
kubectl set image deployment/batch-portal batch-portal="${IMAGE}" -n "${NAMESPACE}"

echo ">>> Waiting for BatchPortal rollout..."
kubectl rollout status deployment/batch-portal -n "${NAMESPACE}"

echo ">>> BatchPortal redeployed with image ${IMAGE}"

