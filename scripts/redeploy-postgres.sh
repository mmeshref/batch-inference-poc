#!/usr/bin/env bash
set -euo pipefail

NAMESPACE="${NAMESPACE:-batch-inference}"

echo ">>> [Postgres] Applying secrets / PVC / deployment / service..."
kubectl apply -n "${NAMESPACE}" -f k8s/postgres/postgres-secret.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/postgres/postgres-pvc.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/postgres/postgres-deployment.yaml
kubectl apply -n "${NAMESPACE}" -f k8s/postgres/postgres-service.yaml

echo ">>> [Postgres] Restarting deployment..."
kubectl rollout restart deployment/postgres -n "${NAMESPACE}"
kubectl rollout status deployment/postgres -n "${NAMESPACE}"

echo ">>> [Postgres] Applying database migrations..."
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [ -f "${SCRIPT_DIR}/apply-deduplication-migration-k8s.sh" ]; then
  "${SCRIPT_DIR}/apply-deduplication-migration-k8s.sh" "${NAMESPACE}"
else
  echo ">>> [Postgres] Applying deduplication migration (inline)..."
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
        RAISE NOTICE 'Skipping: requests table does not exist yet. Schema will be created by EF Core.';
    END IF;
END $$;
SQL
fi

echo ">>> [Postgres] Redeploy complete."