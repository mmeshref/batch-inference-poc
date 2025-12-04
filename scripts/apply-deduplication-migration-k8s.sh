#!/bin/bash
# Script to apply the deduplication migration to PostgreSQL in Kubernetes
# Usage: ./scripts/apply-deduplication-migration-k8s.sh [namespace]

set -e

NAMESPACE="${1:-batch-inference}"

echo ">>> Applying deduplication migration to PostgreSQL in namespace: ${NAMESPACE}"

# Wait for postgres to be ready
echo ">>> Waiting for PostgreSQL to be ready..."
kubectl wait --for=condition=ready pod -l app=postgres -n "${NAMESPACE}" --timeout=60s || {
  echo ">>> Warning: PostgreSQL pod not ready, attempting migration anyway..."
}

# Get the postgres pod name
POSTGRES_POD=$(kubectl get pod -n "${NAMESPACE}" -l app=postgres -o jsonpath='{.items[0].metadata.name}' 2>/dev/null || echo "")

# Get database credentials from secret (defaults to "batch" if secret not found)
POSTGRES_USER=$(kubectl get secret postgres-secret -n "${NAMESPACE}" -o jsonpath='{.data.username}' 2>/dev/null | base64 -d 2>/dev/null || echo "batch")
POSTGRES_DB="batchdb"

if [ -z "$POSTGRES_POD" ]; then
  echo ">>> Error: Could not find PostgreSQL pod in namespace ${NAMESPACE}"
  echo ">>> Attempting to use deployment/postgres instead..."
  kubectl exec -n "${NAMESPACE}" deployment/postgres -- psql -U "${POSTGRES_USER}" -d "${POSTGRES_DB}" << 'SQL'
ALTER TABLE requests ADD COLUMN IF NOT EXISTS "InputHash" TEXT;
ALTER TABLE requests ADD COLUMN IF NOT EXISTS "OriginalRequestId" UUID;
ALTER TABLE requests ADD COLUMN IF NOT EXISTS "IsDeduplicated" BOOLEAN NOT NULL DEFAULT false;
CREATE INDEX IF NOT EXISTS "IX_requests_InputHash" ON requests ("InputHash");
SELECT 'Migration applied successfully!' as status;
SQL
else
  echo ">>> Found PostgreSQL pod: ${POSTGRES_POD}"
  echo ">>> Using database user: ${POSTGRES_USER}"
  
  # Try to copy the migration file first
  SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
  if [ -f "${SCRIPT_DIR}/add-deduplication-columns.sql" ]; then
    echo ">>> Copying migration file to pod..."
    kubectl cp "${SCRIPT_DIR}/add-deduplication-columns.sql" "${NAMESPACE}/${POSTGRES_POD}:/tmp/migration.sql" || {
      echo ">>> File copy failed, applying migration directly..."
      kubectl exec -n "${NAMESPACE}" "${POSTGRES_POD}" -- psql -U "${POSTGRES_USER}" -d "${POSTGRES_DB}" << 'SQL'
ALTER TABLE requests ADD COLUMN IF NOT EXISTS "InputHash" TEXT;
ALTER TABLE requests ADD COLUMN IF NOT EXISTS "OriginalRequestId" UUID;
ALTER TABLE requests ADD COLUMN IF NOT EXISTS "IsDeduplicated" BOOLEAN NOT NULL DEFAULT false;
CREATE INDEX IF NOT EXISTS "IX_requests_InputHash" ON requests ("InputHash");
SELECT 'Migration applied successfully!' as status;
SQL
      exit 0
    }
    
    echo ">>> Executing migration file..."
    kubectl exec -n "${NAMESPACE}" "${POSTGRES_POD}" -- psql -U "${POSTGRES_USER}" -d "${POSTGRES_DB}" -f /tmp/migration.sql
  else
    echo ">>> Migration file not found, applying migration directly..."
    kubectl exec -n "${NAMESPACE}" "${POSTGRES_POD}" -- psql -U "${POSTGRES_USER}" -d "${POSTGRES_DB}" << 'SQL'
ALTER TABLE requests ADD COLUMN IF NOT EXISTS "InputHash" TEXT;
ALTER TABLE requests ADD COLUMN IF NOT EXISTS "OriginalRequestId" UUID;
ALTER TABLE requests ADD COLUMN IF NOT EXISTS "IsDeduplicated" BOOLEAN NOT NULL DEFAULT false;
CREATE INDEX IF NOT EXISTS "IX_requests_InputHash" ON requests ("InputHash");
SELECT 'Migration applied successfully!' as status;
SQL
  fi
fi

echo ">>> Migration completed successfully!"

