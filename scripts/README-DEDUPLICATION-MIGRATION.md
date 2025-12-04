# Deduplication Migration Guide

This migration adds the necessary columns to the `requests` table to support request deduplication.

## What This Migration Does

Adds three new columns to the `requests` table:
- `InputHash` (TEXT, nullable) - SHA256 hash of the input payload
- `OriginalRequestId` (UUID, nullable) - Reference to the original request if this is a duplicate
- `IsDeduplicated` (BOOLEAN, default false) - Flag indicating if the request was deduplicated

Also creates an index on `InputHash` for fast deduplication lookups.

## Running the Migration

### Option 1: Using the Shell Script (Recommended)

```bash
# If running in Kubernetes, get the connection string from a pod
kubectl exec -n batch-inference postgres-0 -- env | grep POSTGRES

# Or use your connection string directly
./scripts/apply-deduplication-migration.sh "Host=localhost;Port=5432;Database=batchdb;Username=postgres;Password=yourpassword"
```

### Option 2: Using psql Directly

```bash
# Connect to your PostgreSQL database
psql "Host=localhost;Port=5432;Database=batchdb;Username=postgres;Password=yourpassword"

# Then run the migration
\i scripts/add-deduplication-columns.sql
```

### Option 3: Using kubectl (If running in Kubernetes)

```bash
# Get the database username from the secret (defaults to "batch")
POSTGRES_USER=$(kubectl get secret postgres-secret -n batch-inference -o jsonpath='{.data.username}' | base64 -d)

# Copy the migration file to the postgres pod
kubectl cp scripts/add-deduplication-columns.sql batch-inference/postgres-0:/tmp/migration.sql

# Execute it with the correct username
kubectl exec -n batch-inference postgres-0 -- psql -U "${POSTGRES_USER}" -d batchdb -f /tmp/migration.sql

# Or use the helper script (recommended):
./scripts/apply-deduplication-migration-k8s.sh
```

### Option 4: Manual SQL Execution

If you have access to a PostgreSQL client, you can copy and paste the SQL from `scripts/add-deduplication-columns.sql` and execute it directly.

## Verifying the Migration

After running the migration, you can verify it worked by checking the columns:

```sql
SELECT column_name, data_type, is_nullable, column_default
FROM information_schema.columns
WHERE table_name = 'requests' 
  AND column_name IN ('InputHash', 'OriginalRequestId', 'IsDeduplicated')
ORDER BY column_name;
```

You should see all three columns listed.

## Troubleshooting

### Error: "column already exists"
This means the migration has already been applied. You can safely ignore this or use `IF NOT EXISTS` clauses (which the script already includes).

### Error: "permission denied"
Make sure you're connecting as a user with ALTER TABLE permissions (typically the database owner or a superuser).

### Error: "relation 'requests' does not exist"
The database schema hasn't been created yet. Run `EnsureCreated()` first or create the initial schema.

## Rollback (If Needed)

If you need to rollback this migration:

```sql
DROP INDEX IF EXISTS "IX_requests_InputHash";
ALTER TABLE requests DROP COLUMN IF EXISTS "IsDeduplicated";
ALTER TABLE requests DROP COLUMN IF EXISTS "OriginalRequestId";
ALTER TABLE requests DROP COLUMN IF EXISTS "InputHash";
```

**Note:** This will remove all deduplication data. Only use this if you're sure you want to remove the feature.

