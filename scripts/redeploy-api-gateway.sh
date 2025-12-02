#!/usr/bin/env bash
set -euo pipefail

# Namespace can be overridden from environment, defaults to doubleword-batch
NAMESPACE="${NAMESPACE:-doubleword-batch}"

# Image tag can be passed as first argument, defaults to v-dev
TAG="${1:-v-dev}"
IMAGE="dwb/api-gateway:${TAG}"

echo ">>> Building ApiGateway image: ${IMAGE}"
docker build -f docker/ApiGateway.Dockerfile -t "${IMAGE}" .

echo ">>> Updating Kubernetes deployment/api-gateway in namespace ${NAMESPACE} to image ${IMAGE}"
kubectl set image deployment/api-gateway api-gateway="${IMAGE}" -n "${NAMESPACE}"

echo ">>> Waiting for ApiGateway rollout to complete..."
kubectl rollout status deployment/api-gateway -n "${NAMESPACE}"

echo ">>> ApiGateway redeployed with image ${IMAGE}"

