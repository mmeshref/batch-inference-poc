#!/bin/bash
# Script to apply the deduplication migration to PostgreSQL
# Usage: 
#   ./scripts/apply-deduplication-migration.sh [connection-string]
#   ./scripts/apply-deduplication-migration.sh  # Uses connection string from appsettings.Development.json

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
MIGRATION_FILE="$SCRIPT_DIR/add-deduplication-columns.sql"

# Get connection string from argument, environment variable, or appsettings
if [ -n "$1" ]; then
    CONNECTION_STRING="$1"
elif [ -n "$POSTGRES_CONNECTION_STRING" ]; then
    CONNECTION_STRING="$POSTGRES_CONNECTION_STRING"
else
    # Try to read from appsettings.Development.json
    if [ -f "${REPO_ROOT}/BatchPortal/appsettings.Development.json" ]; then
        CONNECTION_STRING=$(grep -o '"Postgres":\s*"[^"]*"' "${REPO_ROOT}/BatchPortal/appsettings.Development.json" | sed 's/.*"Postgres":\s*"\([^"]*\)".*/\1/')
        if [ -n "$CONNECTION_STRING" ]; then
            echo ">>> Using connection string from appsettings.Development.json"
        fi
    fi
    
    if [ -z "$CONNECTION_STRING" ]; then
        echo "Error: PostgreSQL connection string required"
        echo ""
        echo "Usage:"
        echo "  $0 <connection-string>"
        echo "  POSTGRES_CONNECTION_STRING='...' $0"
        echo "  $0  # (auto-detects from appsettings.Development.json)"
        echo ""
        echo "Example:"
        echo "  $0 'Host=localhost;Port=5432;Database=batchdb;Username=postgres;Password=password'"
        exit 1
    fi
fi

if [ ! -f "$MIGRATION_FILE" ]; then
    echo "Error: Migration file not found: $MIGRATION_FILE"
    exit 1
fi

echo ">>> Applying deduplication migration..."
echo ">>> Connection: ${CONNECTION_STRING%%@*}@***"  # Mask password in output

# Convert connection string format if needed (psql uses space-separated, not semicolon)
PSQL_CONNECTION=$(echo "$CONNECTION_STRING" | sed 's/;/\n/g' | awk -F'=' '{if ($1=="Host") print "-h "$2; else if ($1=="Port") print "-p "$2; else if ($1=="Database") print "-d "$2; else if ($1=="Username") print "-U "$2}' | tr '\n' ' ')
PSQL_PASSWORD=$(echo "$CONNECTION_STRING" | sed 's/;/\n/g' | awk -F'=' '{if ($1=="Password") print $2}')

if [ -n "$PSQL_PASSWORD" ]; then
    export PGPASSWORD="$PSQL_PASSWORD"
fi

psql $PSQL_CONNECTION -f "$MIGRATION_FILE"

echo ">>> Migration applied successfully!"

