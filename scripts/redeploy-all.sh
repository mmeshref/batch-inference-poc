#!/usr/bin/env bash
set -euo pipefail

TAG="${1:-}"
if [[ -z "$TAG" ]]; then
  echo "Usage: $0 <image-tag>"
  exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
NAMESPACE="batch-inference"

cd "${REPO_ROOT}"

echo ">>> Redeploying EVERYTHING into namespace: ${NAMESPACE} with image tag: ${TAG}"

echo ">>> Deleting namespace (if exists)..."
kubectl delete namespace "${NAMESPACE}" --ignore-not-found=true

echo ">>> Creating namespace..."
kubectl create namespace "${NAMESPACE}"

echo ">>> Applying storage (PVCs)..."
kubectl apply -n "${NAMESPACE}" -f k8s/storage/user-storage-pvc.yaml

echo ">>> Deploying Postgres..."
kubectl apply -n "${NAMESPACE}" -f k8s/postgres/postgres-secret.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/postgres/postgres-pvc.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/postgres/postgres-deployment.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/postgres/postgres-service.yaml

echo ">>> Waiting for Postgres to be ready..."
kubectl rollout status deployment/postgres -n "${NAMESPACE}"

echo ">>> Deploying Prometheus and Alertmanager..."
kubectl apply -n "${NAMESPACE}" -f k8s/monitoring/prometheus-configmap.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/monitoring/alert-rules.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/monitoring/prometheus-deployment.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/monitoring/prometheus-service.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/monitoring/alerting/alertmanager-config.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/monitoring/alerting/alertmanager.yaml

echo ">>> Deploying Grafana..."
kubectl apply -n "${NAMESPACE}" -f k8s/monitoring/grafana-dashboard-providers.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/monitoring/grafana-datasource-config.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/monitoring/grafana-dashboards.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/monitoring/grafana-deployment.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/monitoring/grafana-service.yaml

echo ">>> Deploying ApiGateway (will create schema on startup)..."
kubectl apply -n "${NAMESPACE}" -f k8s/api/api-deployment.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/api/api-service.yaml
API_IMAGE="dwb/api-gateway:${TAG}"
echo ">>> Building ApiGateway image ${API_IMAGE}"
docker build -f docker/ApiGateway.Dockerfile -t "${API_IMAGE}" .
echo ">>> Updating ApiGateway deployment to ${API_IMAGE}"
kubectl set image deployment/api-gateway api-gateway="${API_IMAGE}" -n "${NAMESPACE}" --record=false
echo ">>> Waiting for ApiGateway rollout..."
kubectl rollout status deployment/api-gateway -n "${NAMESPACE}"

echo ">>> Waiting for ApiGateway to initialize database schema..."
sleep 5

echo ">>> Applying database migrations..."
if [ -f "${SCRIPT_DIR}/apply-deduplication-migration-k8s.sh" ]; then
  "${SCRIPT_DIR}/apply-deduplication-migration-k8s.sh" "${NAMESPACE}"
else
  echo ">>> Applying deduplication migration (inline)..."
  # Get username from secret (defaults to "batch")
  POSTGRES_USER=$(kubectl get secret postgres-secret -n "${NAMESPACE}" -o jsonpath='{.data.username}' 2>/dev/null | base64 -d 2>/dev/null || echo "batch")
  kubectl exec -n "${NAMESPACE}" deployment/postgres -- psql -U "${POSTGRES_USER}" -d batchdb << 'SQL'
DO $$
BEGIN
    IF EXISTS (
        SELECT FROM information_schema.tables 
        WHERE table_schema = 'public' 
        AND table_name = 'requests'
    ) THEN
        ALTER TABLE requests ADD COLUMN IF NOT EXISTS "InputHash" TEXT;
        ALTER TABLE requests ADD COLUMN IF NOT EXISTS "OriginalRequestId" UUID;
        ALTER TABLE requests ADD COLUMN IF NOT EXISTS "IsDeduplicated" BOOLEAN NOT NULL DEFAULT false;
        CREATE INDEX IF NOT EXISTS "IX_requests_InputHash" ON requests ("InputHash");
        RAISE NOTICE 'Migration applied successfully!';
    ELSE
        RAISE NOTICE 'Skipping: requests table does not exist yet';
    END IF;
END $$;
SQL
fi

echo ">>> Deploying Scheduler..."
kubectl apply -n "${NAMESPACE}" -f k8s/scheduler/scheduler-deployment.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/scheduler/scheduler-service.yaml
SCHEDULER_IMAGE="dwb/scheduler:${TAG}"
echo ">>> Building Scheduler image ${SCHEDULER_IMAGE}"
docker build -f docker/SchedulerService.Dockerfile -t "${SCHEDULER_IMAGE}" .
echo ">>> Updating Scheduler deployment to ${SCHEDULER_IMAGE}"
kubectl set image deployment/scheduler scheduler="${SCHEDULER_IMAGE}" -n "${NAMESPACE}" --record=false
echo ">>> Waiting for Scheduler rollout..."
kubectl rollout status deployment/scheduler -n "${NAMESPACE}"

echo ">>> Deploying GPU workers..."
kubectl apply -n "${NAMESPACE}" -f k8s/worker/gpu-worker-spot-deployment.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/worker/gpu-worker-spot-service.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/worker/gpu-worker-dedicated-deployment.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/worker/gpu-worker-dedicated-service.yaml
WORKER_IMAGE="dwb/gpu-worker:${TAG}"
echo ">>> Building GPU Worker image ${WORKER_IMAGE}"
docker build -f docker/GpuWorker.Dockerfile -t "${WORKER_IMAGE}" .
echo ">>> Updating GPU worker deployments to ${WORKER_IMAGE}"
kubectl set image deployment/gpu-worker-spot gpu-worker="${WORKER_IMAGE}" -n "${NAMESPACE}" --record=false
kubectl set image deployment/gpu-worker-dedicated gpu-worker="${WORKER_IMAGE}" -n "${NAMESPACE}" --record=false
echo ">>> Waiting for GPU worker rollouts..."
kubectl rollout status deployment/gpu-worker-spot -n "${NAMESPACE}"
kubectl rollout status deployment/gpu-worker-dedicated -n "${NAMESPACE}"

echo ">>> Deploying Batch Portal..."
kubectl apply -n "${NAMESPACE}" -f k8s/portal/batch-portal.yaml
PORTAL_IMAGE="dwb/batch-portal:${TAG}"
echo ">>> Building Batch Portal image ${PORTAL_IMAGE}"
docker build -f docker/BatchPortal.Dockerfile -t "${PORTAL_IMAGE}" .
echo ">>> Updating Batch Portal deployment to ${PORTAL_IMAGE}"
kubectl set image deployment/batch-portal batch-portal="${PORTAL_IMAGE}" -n "${NAMESPACE}" --record=false
echo ">>> Waiting for Batch Portal rollout..."
kubectl rollout status deployment/batch-portal -n "${NAMESPACE}"

echo ">>> Final pod status in namespace ${NAMESPACE}:"
kubectl get pods -n "${NAMESPACE}"

echo ">>> Redeploy complete."