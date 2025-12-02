#!/usr/bin/env bash

set -euo pipefail

NAMESPACE="${NAMESPACE:-doubleword-batch}"
TAG="${1:-v-dev}"
IMAGE="dwb/scheduler:${TAG}"

echo ">>> Building SchedulerService image: ${IMAGE}"
docker build -f docker/SchedulerService.Dockerfile -t "${IMAGE}" .

echo ">>> Updating Kubernetes deployment/scheduler in namespace ${NAMESPACE} to image ${IMAGE}"
kubectl set image deployment/scheduler scheduler="${IMAGE}" -n "${NAMESPACE}"

echo ">>> Waiting for Scheduler deployment rollout..."
kubectl rollout status deployment/scheduler -n "${NAMESPACE}"

echo ">>> SchedulerService redeployed with image ${IMAGE}"

