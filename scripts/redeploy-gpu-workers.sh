#!/usr/bin/env bash

set -euo pipefail

NAMESPACE="${NAMESPACE:-doubleword-batch}"
TAG="${1:-v-dev}"
IMAGE="dwb/gpu-worker:${TAG}"

echo ">>> Building GpuWorker image: ${IMAGE}"
docker build -f docker/GpuWorker.Dockerfile -t "${IMAGE}" .

echo ">>> [Workers] Applying worker deployment + service manifests..."
kubectl apply -n "${NAMESPACE}" -f k8s/worker/gpu-worker-dedicated-deployment.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/worker/gpu-worker-dedicated-service.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/worker/gpu-worker-spot-deployment.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/worker/gpu-worker-spot-service.yaml

echo ">>> Updating Kubernetes deployments for GPU workers in namespace ${NAMESPACE} to image ${IMAGE}"
kubectl set image deployment/gpu-worker-spot gpu-worker="${IMAGE}" -n "${NAMESPACE}"
kubectl set image deployment/gpu-worker-dedicated gpu-worker="${IMAGE}" -n "${NAMESPACE}"

echo ">>> Waiting for gpu-worker-spot rollout..."
kubectl rollout status deployment/gpu-worker-spot -n "${NAMESPACE}"

echo ">>> Waiting for gpu-worker-dedicated rollout..."
kubectl rollout status deployment/gpu-worker-dedicated -n "${NAMESPACE}"

echo ">>> GpuWorker redeployed (spot + dedicated) with image ${IMAGE}"

