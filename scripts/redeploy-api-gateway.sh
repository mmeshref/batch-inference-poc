#!/usr/bin/env bash
set -euo pipefail

NAMESPACE="${NAMESPACE:-doubleword-batch}"
TAG="${1:-v-dev}"
IMAGE="dwb/api-gateway:${TAG}"

echo ">>> [API] Building image: ${IMAGE}"
docker build -f docker/ApiGateway.Dockerfile -t "${IMAGE}" .

echo ">>> [API] Applying deployment + service manifests..."
kubectl apply -n "${NAMESPACE}" -f k8s/api/api-deployment.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/api/api-service.yaml

echo ">>> [API] Updating deployment image..."
kubectl set image deployment/api-gateway api-gateway="${IMAGE}" -n "${NAMESPACE}"

echo ">>> [API] Waiting for rollout..."
kubectl rollout status deployment/api-gateway -n "${NAMESPACE}"

echo ">>> [API] Redeploy complete."