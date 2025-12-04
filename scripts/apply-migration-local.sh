#!/bin/bash
# Quick script to apply deduplication migration to local database
# This reads the connection string from BatchPortal/appsettings.Development.json

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

echo ">>> Applying deduplication migration to local database..."

# Read connection string from appsettings.Development.json
if [ ! -f "${REPO_ROOT}/BatchPortal/appsettings.Development.json" ]; then
    echo "Error: appsettings.Development.json not found"
    exit 1
fi

CONNECTION_STRING=$(grep -o '"Postgres":\s*"[^"]*"' "${REPO_ROOT}/BatchPortal/appsettings.Development.json" | sed 's/.*"Postgres":\s*"\([^"]*\)".*/\1/')

if [ -z "$CONNECTION_STRING" ]; then
    echo "Error: Could not find Postgres connection string in appsettings.Development.json"
    exit 1
fi

echo ">>> Found connection string: ${CONNECTION_STRING%%@*}@***"

# Extract connection parameters
HOST=$(echo "$CONNECTION_STRING" | sed 's/.*Host=\([^;]*\).*/\1/')
PORT=$(echo "$CONNECTION_STRING" | sed 's/.*Port=\([^;]*\).*/\1/')
DATABASE=$(echo "$CONNECTION_STRING" | sed 's/.*Database=\([^;]*\).*/\1/')
USERNAME=$(echo "$CONNECTION_STRING" | sed 's/.*Username=\([^;]*\).*/\1/')
PASSWORD=$(echo "$CONNECTION_STRING" | sed 's/.*Password=\([^;]*\).*/\1/')

export PGPASSWORD="$PASSWORD"

psql -h "$HOST" -p "$PORT" -U "$USERNAME" -d "$DATABASE" << 'SQL'
ALTER TABLE requests ADD COLUMN IF NOT EXISTS "InputHash" TEXT;
ALTER TABLE requests ADD COLUMN IF NOT EXISTS "OriginalRequestId" UUID;
ALTER TABLE requests ADD COLUMN IF NOT EXISTS "IsDeduplicated" BOOLEAN NOT NULL DEFAULT false;
CREATE INDEX IF NOT EXISTS "IX_requests_InputHash" ON requests ("InputHash");
SELECT 'Migration applied successfully!' as status;
SQL

echo ">>> Migration completed!"

