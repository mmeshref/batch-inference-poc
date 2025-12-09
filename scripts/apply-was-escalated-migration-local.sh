#!/bin/bash
# Quick script to apply WasEscalated migration to local database
# This reads the connection string from BatchPortal/appsettings.Development.json

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

echo ">>> Applying WasEscalated migration to local database..."

# Read connection string from appsettings.Development.json
if [ ! -f "${REPO_ROOT}/BatchPortal/appsettings.Development.json" ]; then
    echo "Error: appsettings.Development.json not found"
    exit 1
fi

CONNECTION_STRING=$(grep -o '"Postgres":\s*"[^"]*"' "${REPO_ROOT}/BatchPortal/appsettings.Development.json" | sed 's/.*"Postgres":\s*"\([^"]*\)".*/\1/' | sed 's/^"//' | sed 's/"$//')

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
DO $$
BEGIN
    IF EXISTS (
        SELECT FROM information_schema.tables 
        WHERE table_schema = 'public' 
        AND table_name = 'requests'
    ) THEN
        ALTER TABLE requests ADD COLUMN IF NOT EXISTS "WasEscalated" BOOLEAN NOT NULL DEFAULT false;
        RAISE NOTICE 'Migration applied: Added WasEscalated column to requests table';
    ELSE
        RAISE NOTICE 'Skipping: requests table does not exist yet. Schema will be created by EF Core.';
    END IF;
END $$;
SELECT 'Migration applied successfully!' as status;
SQL

echo ">>> Migration completed!"
