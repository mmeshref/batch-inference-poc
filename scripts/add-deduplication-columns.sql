-- Migration script to add deduplication columns to the requests table
-- Run this script against your PostgreSQL database to add the new columns
-- This script is idempotent and will skip if the table doesn't exist yet

-- Check if the requests table exists before attempting to alter it
DO $$
BEGIN
    IF EXISTS (
        SELECT FROM information_schema.tables 
        WHERE table_schema = 'public' 
        AND table_name = 'requests'
    ) THEN
        -- Add InputHash column (nullable, for storing SHA256 hash of input payload)
        ALTER TABLE requests 
        ADD COLUMN IF NOT EXISTS "InputHash" TEXT;

        -- Add OriginalRequestId column (nullable, references the original request if this is a duplicate)
        ALTER TABLE requests 
        ADD COLUMN IF NOT EXISTS "OriginalRequestId" UUID;

        -- Add IsDeduplicated column (boolean, default false)
        ALTER TABLE requests 
        ADD COLUMN IF NOT EXISTS "IsDeduplicated" BOOLEAN NOT NULL DEFAULT false;

        -- Create index on InputHash for fast deduplication lookups
        CREATE INDEX IF NOT EXISTS "IX_requests_InputHash" ON requests ("InputHash");

        RAISE NOTICE 'Migration applied: Added deduplication columns to requests table';
    ELSE
        RAISE NOTICE 'Skipping migration: requests table does not exist yet. Schema will be created by EF Core on first application startup.';
    END IF;
END $$;

-- Verify the columns were added (only if table exists)
SELECT column_name, data_type, is_nullable, column_default
FROM information_schema.columns
WHERE table_name = 'requests' 
  AND column_name IN ('InputHash', 'OriginalRequestId', 'IsDeduplicated')
ORDER BY column_name;

